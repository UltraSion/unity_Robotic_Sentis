using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ne.MjcfImporter;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    public sealed class ArticulationRobotBuildResult
    {
        public GameObject Root;
        public List<Mesh> Meshes = new();
    }

    sealed class BuildContext
    {
        public readonly Dictionary<string, ArticulationBody> JointBodies = new();
    }

    public static class ArticulationRobotBuilder
    {
        public static ArticulationRobotBuildResult Build(MjcfRobotAsset asset)
        {
            var result = new ArticulationRobotBuildResult();
            var context = new BuildContext();
            string rootName = string.IsNullOrWhiteSpace(asset.modelName) ? "MjcfRobot" : asset.modelName;
            result.Root = new GameObject(rootName);

            foreach (MjcfBody body in asset.rootBodies)
            {
                BuildBody(body, result.Root.transform, asset, result, context, true);
            }

            AddActuatorController(result.Root, asset, context);
            return result;
        }

        static Transform BuildBody(MjcfBody body, Transform parent, MjcfRobotAsset asset, ArticulationRobotBuildResult result, BuildContext context, bool isRootBody)
        {
            List<MjcfJoint> movableJoints = body.joints.Where(joint => joint.type != MjcfJointType.Free).ToList();
            bool hasFreeJoint = body.joints.Any(joint => joint.type == MjcfJointType.Free);
            Transform currentParent = parent;
            Transform bodyTransform;

            if (movableJoints.Count == 0)
            {
                GameObject bodyObject = CreateChild(body.name, currentParent, body.unityPosition, body.unityRotation);
                ArticulationBody articulation = bodyObject.AddComponent<ArticulationBody>();
                if (isRootBody)
                {
                    articulation.immovable = !hasFreeJoint;
                }
                ConfigureFixedJoint(articulation);
                bodyTransform = bodyObject.transform;
            }
            else
            {
                bodyTransform = null;
                for (int i = 0; i < movableJoints.Count; i++)
                {
                    MjcfJoint joint = movableJoints[i];
                    bool isLastJoint = i == movableJoints.Count - 1;
                    string nodeName = isLastJoint ? body.name : $"{body.name}__joint_{i}_{joint.name}";
                    Vector3 position = i == 0 ? body.unityPosition : Vector3.zero;
                    Quaternion rotation = i == 0 ? body.unityRotation : Quaternion.identity;
                    GameObject jointObject = CreateChild(nodeName, currentParent, position, rotation);
                    ArticulationBody articulation = jointObject.AddComponent<ArticulationBody>();
                    ConfigureJoint(articulation, joint, asset, isRootBody && i == 0, position, rotation);
                    if (!string.IsNullOrWhiteSpace(joint.name))
                    {
                        context.JointBodies[joint.name] = articulation;
                    }
                    currentParent = jointObject.transform;
                    bodyTransform = jointObject.transform;
                }
            }

            if (bodyTransform == null)
            {
                return parent;
            }

            ApplyBodyMass(body, bodyTransform.GetComponent<ArticulationBody>(), asset.importSettings);
            BuildGeoms(body, bodyTransform, asset.importSettings, result);

            foreach (MjcfBody child in body.children)
            {
                BuildBody(child, bodyTransform, asset, result, context, false);
            }

            return bodyTransform;
        }

        static void AddActuatorController(GameObject root, MjcfRobotAsset asset, BuildContext context)
        {
            if (asset.actuators.Count == 0)
            {
                return;
            }

            MjcfActuatorController controller = root.AddComponent<MjcfActuatorController>();
            controller.robotAsset = asset;

            foreach (MjcfActuator actuator in asset.actuators)
            {
                context.JointBodies.TryGetValue(actuator.jointName ?? string.Empty, out ArticulationBody body);
                controller.bindings.Add(new MjcfActuatorController.Binding
                {
                    actuatorName = actuator.name,
                    jointName = actuator.jointName,
                    type = actuator.type,
                    articulationBody = body,
                    gear = GetUnityControlScale(actuator, body, asset),
                    ctrlLimited = actuator.ctrlLimited,
                    ctrlRange = actuator.ctrlRange
                });
            }
        }

        static float GetUnityControlScale(MjcfActuator actuator, ArticulationBody body, MjcfRobotAsset asset)
        {
            float gear = actuator.gear == 0f ? 1f : actuator.gear;
            if (body == null || asset.angleMode != MjcfAngleMode.Radian)
            {
                return gear;
            }

            bool angularDrive =
                body.jointType == ArticulationJointType.RevoluteJoint ||
                body.jointType == ArticulationJointType.SphericalJoint;
            bool angularActuator =
                actuator.type == MjcfActuatorType.Position ||
                actuator.type == MjcfActuatorType.Velocity;

            return angularDrive && angularActuator ? gear * Mathf.Rad2Deg : gear;
        }

        static GameObject CreateChild(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var gameObject = new GameObject(SanitizeName(name));
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = localRotation;
            return gameObject;
        }

        static void ConfigureFixedJoint(ArticulationBody articulation)
        {
            articulation.jointType = ArticulationJointType.FixedJoint;
            articulation.mass = Mathf.Max(0.0001f, articulation.mass);
        }

        static void ConfigureJoint(ArticulationBody articulation, MjcfJoint joint, MjcfRobotAsset asset, bool isRoot, Vector3 localPosition, Quaternion localRotation)
        {
            if (isRoot)
            {
                articulation.immovable = false;
            }

            Quaternion anchorRotation = MjcfCoordinateSystem.AxisToXDriveRotation(joint.unityAxis);
            articulation.matchAnchors = false;
            articulation.anchorPosition = joint.unityPosition;
            articulation.parentAnchorPosition = localPosition + localRotation * joint.unityPosition;
            articulation.anchorRotation = anchorRotation;
            articulation.parentAnchorRotation = anchorRotation;

            switch (joint.type)
            {
                case MjcfJointType.Hinge:
                    articulation.jointType = ArticulationJointType.RevoluteJoint;
                    ConfigureDrive(articulation, joint, asset);
                    break;
                case MjcfJointType.Slide:
                    articulation.jointType = ArticulationJointType.PrismaticJoint;
                    ConfigureDrive(articulation, joint, asset);
                    break;
                case MjcfJointType.Ball:
                    articulation.jointType = ArticulationJointType.SphericalJoint;
                    ConfigureSphericalDrive(articulation, joint, asset);
                    break;
                default:
                    articulation.jointType = ArticulationJointType.FixedJoint;
                    break;
            }
        }

        static void ConfigureDrive(ArticulationBody articulation, MjcfJoint joint, MjcfRobotAsset asset)
        {
            MjcfImportSettings settings = asset.importSettings;
            ArticulationDrive drive = articulation.xDrive;
            if (settings.applyJointLimits && joint.limited)
            {
                drive.lowerLimit = joint.range.x;
                drive.upperLimit = joint.range.y;
            }
            else
            {
                drive.lowerLimit = -180f;
                drive.upperLimit = 180f;
            }

            if (settings.applyStiffnessDamping)
            {
                drive.stiffness = joint.stiffness * settings.stiffnessScale;
                drive.damping = joint.damping * settings.dampingScale;
            }

            ApplyActuatorDriveDefaults(ref drive, joint, asset);

            drive.forceLimit = float.MaxValue;
            articulation.xDrive = drive;
        }

        static void ConfigureSphericalDrive(ArticulationBody articulation, MjcfJoint joint, MjcfRobotAsset asset)
        {
            MjcfImportSettings settings = asset.importSettings;
            ArticulationDrive drive = articulation.xDrive;
            if (settings.applyStiffnessDamping)
            {
                drive.stiffness = joint.stiffness * settings.stiffnessScale;
                drive.damping = joint.damping * settings.dampingScale;
            }
            ApplyActuatorDriveDefaults(ref drive, joint, asset);
            drive.forceLimit = float.MaxValue;
            articulation.xDrive = drive;
            articulation.yDrive = drive;
            articulation.zDrive = drive;
        }

        static void ApplyActuatorDriveDefaults(ref ArticulationDrive drive, MjcfJoint joint, MjcfRobotAsset asset)
        {
            foreach (MjcfActuator actuator in asset.actuators)
            {
                if (!string.Equals(actuator.jointName, joint.name, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (actuator.type == MjcfActuatorType.Position && actuator.kp > 0f)
                {
                    drive.stiffness = actuator.kp * asset.importSettings.stiffnessScale;
                }
                else if (actuator.type == MjcfActuatorType.Velocity && actuator.kv > 0f)
                {
                    drive.damping = actuator.kv * asset.importSettings.dampingScale;
                }
            }
        }

        static void BuildGeoms(MjcfBody body, Transform parent, MjcfImportSettings settings, ArticulationRobotBuildResult result)
        {
            foreach (MjcfGeom geom in body.geoms)
            {
                GameObject geomObject = CreateChild(geom.name, parent, geom.unityCenter, geom.unityRotation);

                if (settings.generateVisualGeometry)
                {
                    Mesh mesh = CreateMesh(geom);
                    if (mesh != null)
                    {
                        mesh.name = $"{SanitizeName(body.name)}_{SanitizeName(geom.name)}_{geom.type}";
                        result.Meshes.Add(mesh);
                        geomObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        MeshRenderer renderer = geomObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                    }
                }

                if (settings.generateColliders)
                {
                    AddCollider(geomObject, geom);
                }
            }
        }

        static void ApplyBodyMass(MjcfBody body, ArticulationBody articulation, MjcfImportSettings settings)
        {
            if (articulation == null)
            {
                return;
            }

            float mass = 0f;
            foreach (MjcfGeom geom in body.geoms)
            {
                if (geom.mass > 0f)
                {
                    mass += geom.mass * settings.massScale;
                    continue;
                }

                float volume = CalculateVolume(geom);
                if (volume > 0f)
                {
                    mass += volume * Mathf.Max(0f, geom.density) * settings.massScale;
                }
            }

            articulation.mass = Mathf.Max(0.0001f, mass > 0f ? mass : articulation.mass);
        }

        static float CalculateVolume(MjcfGeom geom)
        {
            switch (geom.type)
            {
                case MjcfGeomType.Box:
                {
                    Vector3 fullSize = geom.unityHalfExtents * 2f;
                    return Mathf.Max(0f, fullSize.x * fullSize.y * fullSize.z);
                }
                case MjcfGeomType.Sphere:
                {
                    float radius = Mathf.Max(0f, geom.unityRadius);
                    return 4f / 3f * Mathf.PI * radius * radius * radius;
                }
                case MjcfGeomType.Capsule:
                {
                    float radius = Mathf.Max(0f, geom.unityRadius);
                    float cylinderHeight = Mathf.Max(0f, geom.unityCylinderLength);
                    return Mathf.PI * radius * radius * cylinderHeight + 4f / 3f * Mathf.PI * radius * radius * radius;
                }
                case MjcfGeomType.Cylinder:
                {
                    float radius = Mathf.Max(0f, geom.unityRadius);
                    float height = Mathf.Max(0f, geom.unityLength);
                    return Mathf.PI * radius * radius * Mathf.Max(0f, height);
                }
                default:
                    return 0f;
            }
        }

        static Mesh CreateMesh(MjcfGeom geom)
        {
            switch (geom.type)
            {
                case MjcfGeomType.Box:
                    return PrimitiveMeshBuilder.CreateBox(geom.unityHalfExtents * 2f);
                case MjcfGeomType.Sphere:
                    return PrimitiveMeshBuilder.CreateSphere(Mathf.Max(0.0001f, geom.unityRadius));
                case MjcfGeomType.Capsule:
                {
                    float radius = Mathf.Max(0.0001f, geom.unityRadius);
                    float height = Mathf.Max(radius * 2f, geom.unityLength);
                    return PrimitiveMeshBuilder.CreateCapsule(radius, height);
                }
                case MjcfGeomType.Cylinder:
                {
                    float radius = Mathf.Max(0.0001f, geom.unityRadius);
                    float height = Mathf.Max(0.0001f, geom.unityLength);
                    return PrimitiveMeshBuilder.CreateCylinder(radius, height);
                }
                default:
                    return null;
            }
        }

        static void AddCollider(GameObject geomObject, MjcfGeom geom)
        {
            switch (geom.type)
            {
                case MjcfGeomType.Box:
                    BoxCollider box = geomObject.AddComponent<BoxCollider>();
                    box.size = geom.unityHalfExtents * 2f;
                    break;
                case MjcfGeomType.Sphere:
                    SphereCollider sphere = geomObject.AddComponent<SphereCollider>();
                    sphere.radius = Mathf.Max(0.0001f, geom.unityRadius);
                    break;
                case MjcfGeomType.Capsule:
                    CapsuleCollider capsule = geomObject.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = Mathf.Max(0.0001f, geom.unityRadius);
                    capsule.height = Mathf.Max(capsule.radius * 2f, geom.unityLength);
                    break;
                case MjcfGeomType.Cylinder:
                    MeshCollider meshCollider = geomObject.AddComponent<MeshCollider>();
                    MeshFilter filter = geomObject.GetComponent<MeshFilter>();
                    meshCollider.sharedMesh = filter != null ? filter.sharedMesh : CreateMesh(geom);
                    meshCollider.convex = true;
                    break;
            }
        }

        static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "MjcfNode";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}
