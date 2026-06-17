using System.IO;
using System.Linq;
using Ne.MjcfImporter;
using NUnit.Framework;
using UnityEngine;

namespace Ne.MjcfImporter.Editor.Tests
{
    public sealed class MjcfImporterEditorTests
    {
        [Test]
        public void CoordinateVectorConvertsFromMjcfZUpToUnityYUp()
        {
            Vector3 converted = MjcfCoordinateSystem.ToUnityVector(new Vector3(1f, 2f, 3f));
            AssertVector(new Vector3(-2f, 3f, 1f), converted);
        }

        [Test]
        public void IdentityRotationRemainsIdentityAfterBasisConversion()
        {
            Quaternion converted = MjcfCoordinateSystem.ToUnityRotation(Quaternion.identity);
            Assert.That(Quaternion.Angle(Quaternion.identity, converted), Is.LessThan(0.001f));
        }

        [Test]
        public void ParserConvertsFromToCapsuleEndpoints()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='capsule'><worldbody><body name='root'>" +
                "<geom name='limb' type='capsule' fromto='1 2 3 4 5 6' size='0.1'/>" +
                "</body></worldbody></mujoco>");

            MjcfGeom geom = asset.rootBodies[0].geoms[0];
            AssertVector(new Vector3(-2f, 3f, 1f), geom.unityFrom);
            AssertVector(new Vector3(-5f, 6f, 4f), geom.unityTo);
            AssertVector(new Vector3(-3.5f, 4.5f, 2.5f), geom.unityPosition);
        }

        [Test]
        public void FromToCapsuleUsesSegmentLengthPlusDiameter()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='capsule'><worldbody><body name='root'>" +
                "<geom name='limb' type='capsule' fromto='0 0 0 0 0 1' size='0.1'/>" +
                "</body></worldbody></mujoco>");

            MjcfGeom geom = asset.rootBodies[0].geoms[0];
            Assert.That(geom.unityRadius, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(geom.unityCylinderLength, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(geom.unityLength, Is.EqualTo(1.2f).Within(0.0001f));

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                CapsuleCollider collider = result.Root.transform.Find("root/limb").GetComponent<CapsuleCollider>();
                Assert.That(collider.height, Is.EqualTo(1.2f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void CapsuleAndCylinderSizeUseMjcfHalfLengthSemantics()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='sizes'><worldbody><body name='root'>" +
                "<geom name='capsule' type='capsule' size='0.1 0.5'/>" +
                "<geom name='cylinder' type='cylinder' size='0.1 0.5'/>" +
                "<geom name='box' type='box' size='1 2 3'/>" +
                "</body></worldbody></mujoco>");

            MjcfBody body = asset.rootBodies[0];
            Assert.That(body.geoms[0].unityLength, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(body.geoms[0].unityCylinderLength, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(body.geoms[1].unityLength, Is.EqualTo(1f).Within(0.0001f));
            AssertVector(new Vector3(2f, 3f, 1f), body.geoms[2].unityHalfExtents);
        }

        [Test]
        public void BuilderCreatesSingleBodyBoxVisualAndCollider()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='box'><worldbody><body name='root'>" +
                "<geom name='box_geom' type='box' pos='1 2 3' size='0.1 0.2 0.3'/>" +
                "</body></worldbody></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform body = result.Root.transform.Find("root");
                Assert.That(body, Is.Not.Null);
                Transform geom = body.Find("box_geom");
                Assert.That(geom, Is.Not.Null);
                Assert.That(geom.GetComponent<MeshFilter>(), Is.Not.Null);
                Assert.That(geom.GetComponent<BoxCollider>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void BuilderAppliesPrimitiveMassFromDensityAndExplicitMass()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='mass'><worldbody><body name='root'>" +
                "<geom name='density_box' type='box' size='0.5 0.25 0.125' density='8'/>" +
                "<body name='child'><geom name='explicit_sphere' type='sphere' size='0.1' mass='3'/></body>" +
                "</body></worldbody></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                ArticulationBody rootBody = result.Root.transform.Find("root").GetComponent<ArticulationBody>();
                ArticulationBody childBody = result.Root.transform.Find("root/child").GetComponent<ArticulationBody>();
                Assert.That(rootBody.mass, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(childBody.mass, Is.EqualTo(3f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void BuilderAppliesCapsuleAndCylinderMassFromMjcfGeometry()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='mass'><worldbody><body name='root'>" +
                "<geom name='capsule' type='capsule' size='0.1 0.5' density='10'/>" +
                "<geom name='cylinder' type='cylinder' size='0.2 0.25' density='20'/>" +
                "</body></worldbody></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                float capsuleVolume = Mathf.PI * 0.1f * 0.1f * 1f + 4f / 3f * Mathf.PI * 0.1f * 0.1f * 0.1f;
                float cylinderVolume = Mathf.PI * 0.2f * 0.2f * 0.5f;
                float expectedMass = capsuleVolume * 10f + cylinderVolume * 20f;
                ArticulationBody rootBody = result.Root.transform.Find("root").GetComponent<ArticulationBody>();
                Assert.That(rootBody.mass, Is.EqualTo(expectedMass).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void BuilderPreservesParentChildBodyHierarchy()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='tree'><worldbody><body name='parent' pos='0 0 1'>" +
                "<body name='child' pos='1 0 0'/></body></worldbody></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform parent = result.Root.transform.Find("parent");
                Transform child = parent.Find("child");
                Assert.That(child, Is.Not.Null);
                AssertVector(new Vector3(0f, 0f, 1f), child.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void JointPositionIsStoredAndAppliedToArticulationAnchor()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='jointpos'><worldbody><body name='root'><body name='arm' pos='0 0 1'>" +
                "<joint name='hinge' type='hinge' pos='1 2 3' axis='0 0 1'/>" +
                "</body></body></worldbody></mujoco>");

            MjcfJoint joint = asset.rootBodies[0].children[0].joints[0];
            AssertVector(new Vector3(-2f, 3f, 1f), joint.unityPosition);

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                ArticulationBody articulation = result.Root.transform.Find("root/arm").GetComponent<ArticulationBody>();
                AssertVector(new Vector3(-2f, 3f, 1f), articulation.anchorPosition);
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void MultipleHingeJointsCreateIntermediateJointNodes()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='multi'><compiler angle='radian'/><worldbody><body name='root'>" +
                "<body name='arm' pos='0 0 1'>" +
                "<joint name='hinge_x' type='hinge' axis='1 0 0' range='-1 1' stiffness='10' damping='2'/>" +
                "<joint name='hinge_z' type='hinge' axis='0 0 1' range='-0.5 0.5' stiffness='20' damping='3'/>" +
                "<geom name='arm_geom' type='sphere' size='0.1'/>" +
                "</body></body></worldbody></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform root = result.Root.transform.Find("root");
                Transform firstJoint = root.Find("arm__joint_0_hinge_x");
                Transform arm = firstJoint.Find("arm");
                Assert.That(firstJoint, Is.Not.Null);
                Assert.That(arm, Is.Not.Null);
                Assert.That(arm.Find("arm_geom"), Is.Not.Null);

                ArticulationBody articulation = arm.GetComponent<ArticulationBody>();
                Assert.That(articulation.jointType, Is.EqualTo(ArticulationJointType.RevoluteJoint));
                Assert.That(articulation.xDrive.stiffness, Is.EqualTo(20f).Within(0.001f));
                Assert.That(articulation.xDrive.damping, Is.EqualTo(3f).Within(0.001f));
                Assert.That(articulation.xDrive.lowerLimit, Is.EqualTo(-0.5f * Mathf.Rad2Deg).Within(0.001f));
                Assert.That(articulation.xDrive.upperLimit, Is.EqualTo(0.5f * Mathf.Rad2Deg).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void RotationParsesEulerSequenceXyAxesAndZAxis()
        {
            MjcfRobotAsset xyz = Parse(
                "<mujoco model='xyz'><compiler eulerseq='xyz'/><worldbody><body name='root'>" +
                "<geom name='g' type='box' size='1 1 1' euler='10 20 30'/>" +
                "</body></worldbody></mujoco>");
            MjcfRobotAsset zyx = Parse(
                "<mujoco model='zyx'><compiler eulerseq='zyx'/><worldbody><body name='root'>" +
                "<geom name='g' type='box' size='1 1 1' euler='10 20 30'/>" +
                "</body></worldbody></mujoco>");
            Assert.That(Quaternion.Angle(xyz.rootBodies[0].geoms[0].unityRotation, zyx.rootBodies[0].geoms[0].unityRotation), Is.GreaterThan(0.1f));

            MjcfRobotAsset framed = Parse(
                "<mujoco model='frames'><worldbody><body name='root'>" +
                "<geom name='xy' type='box' size='1 1 1' xyaxes='0 1 0 0 0 1'/>" +
                "<geom name='z' type='box' size='1 1 1' zaxis='0 1 0'/>" +
                "</body></worldbody></mujoco>");
            Assert.That(Quaternion.Angle(Quaternion.identity, framed.rootBodies[0].geoms[0].unityRotation), Is.GreaterThan(0.1f));
            Assert.That(Quaternion.Angle(Quaternion.identity, framed.rootBodies[0].geoms[1].unityRotation), Is.GreaterThan(0.1f));
        }

        [Test]
        public void SampleHumanoidXmlParsesAndBuildsStandingHierarchy()
        {
            string samplePath = Path.GetFullPath("Assets/MJCF/humanoid.xml");
            Assert.That(File.Exists(samplePath), Is.True, "Expected sample humanoid XML in Assets/MJCF.");

            MjcfRobotAsset asset = MjcfParser.ParseFile(samplePath, new MjcfImportSettings());
            Assert.That(asset.rootBodies[0].name, Is.EqualTo("pelvis"));
            Assert.That(asset.rootBodies[0].children.Any(child => child.name == "torso"), Is.True);
            Assert.That(asset.actuators.Count, Is.GreaterThan(0));
            Assert.That(asset.actuators.All(actuator => actuator.type == MjcfActuatorType.Motor), Is.True);
            Assert.That(asset.actuators[0].ctrlLimited, Is.True);
            Assert.That(asset.actuators[0].ctrlRange.x, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(asset.actuators[0].ctrlRange.y, Is.EqualTo(1f).Within(0.0001f));
            MjcfGeom rightUpperArm = asset.rootBodies[0].children.Single(child => child.name == "torso")
                .children.Single(child => child.name == "right_upper_arm")
                .geoms.Single(geom => geom.name == "right_upper_arm");
            Assert.That(rightUpperArm.unityCylinderLength, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(rightUpperArm.unityLength, Is.EqualTo(0.29f).Within(0.0001f));

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform pelvis = result.Root.transform.Find("pelvis");
                Assert.That(pelvis, Is.Not.Null);
                Transform torsoJoint0 = pelvis.Find("torso__joint_0_abdomen_x");
                Transform torsoJoint1 = torsoJoint0.Find("torso__joint_1_abdomen_y");
                Transform torso = torsoJoint1.Find("torso");
                Assert.That(torso, Is.Not.Null);
                Assert.That(torso.Find("torso"), Is.Not.Null);
                Assert.That(torso.Find("right_clavicle"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void ParserStoresPositionVelocityAndMotorActuators()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='act'><compiler angle='radian'/>" +
                "<worldbody><body name='root'><body name='arm'><joint name='hinge' type='hinge'/></body></body></worldbody>" +
                "<actuator>" +
                "<position name='pos' joint='hinge' kp='30' gear='2' ctrlrange='-1 1' ctrllimited='true'/>" +
                "<velocity name='vel' joint='hinge' kv='4' gear='3' ctrlrange='-2 2' ctrllimited='true'/>" +
                "<motor name='mot' joint='hinge' gear='5'/>" +
                "</actuator></mujoco>");

            Assert.That(asset.actuators.Count, Is.EqualTo(3));
            Assert.That(asset.actuators[0].type, Is.EqualTo(MjcfActuatorType.Position));
            Assert.That(asset.actuators[0].jointName, Is.EqualTo("hinge"));
            Assert.That(asset.actuators[0].gear, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(asset.actuators[0].kp, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(asset.actuators[0].ctrlRange.x, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(asset.actuators[1].type, Is.EqualTo(MjcfActuatorType.Velocity));
            Assert.That(asset.actuators[1].kv, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(asset.actuators[2].type, Is.EqualTo(MjcfActuatorType.Motor));
        }

        [Test]
        public void PositionAndVelocityActuatorsDriveArticulationTargetValues()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='act'><compiler angle='radian'/>" +
                "<worldbody><body name='root'><body name='arm'>" +
                "<joint name='hinge' type='hinge' axis='0 0 1' stiffness='1' damping='1'/>" +
                "</body></body></worldbody>" +
                "<actuator>" +
                "<position name='pos' joint='hinge' kp='30' gear='2' ctrlrange='-1 1' ctrllimited='true'/>" +
                "<velocity name='vel' joint='hinge' kv='4' gear='3' ctrlrange='-2 2' ctrllimited='true'/>" +
                "</actuator></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform arm = result.Root.transform.Find("root/arm");
                ArticulationBody articulation = arm.GetComponent<ArticulationBody>();
                MjcfActuatorController controller = result.Root.GetComponent<MjcfActuatorController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(controller.bindings.Count, Is.EqualTo(2));

                ArticulationDrive drive = articulation.xDrive;
                Assert.That(drive.stiffness, Is.EqualTo(30f).Within(0.0001f));
                Assert.That(drive.damping, Is.EqualTo(4f).Within(0.0001f));

                Assert.That(controller.SetControl("pos", 2f), Is.True);
                drive = articulation.xDrive;
                Assert.That(drive.target, Is.EqualTo(2f * Mathf.Rad2Deg).Within(0.001f));

                Assert.That(controller.SetControl("vel", -3f), Is.True);
                drive = articulation.xDrive;
                Assert.That(drive.targetVelocity, Is.EqualTo(-6f * Mathf.Rad2Deg).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        [Test]
        public void MotorActuatorIsBoundButDoesNotDriveTargets()
        {
            MjcfRobotAsset asset = Parse(
                "<mujoco model='motor'><worldbody><body name='root'><body name='arm'>" +
                "<joint name='hinge' type='hinge' axis='0 0 1'/>" +
                "</body></body></worldbody>" +
                "<actuator><motor name='mot' joint='hinge' gear='5'/></actuator></mujoco>");

            ArticulationRobotBuildResult result = ArticulationRobotBuilder.Build(asset);
            try
            {
                Transform arm = result.Root.transform.Find("root/arm");
                ArticulationBody articulation = arm.GetComponent<ArticulationBody>();
                MjcfActuatorController controller = result.Root.GetComponent<MjcfActuatorController>();
                Assert.That(controller.bindings.Count, Is.EqualTo(1));
                Assert.That(controller.bindings[0].type, Is.EqualTo(MjcfActuatorType.Motor));

                ArticulationDrive before = articulation.xDrive;
                Assert.That(controller.SetControl("mot", 5f), Is.True);
                ArticulationDrive after = articulation.xDrive;
                Assert.That(after.target, Is.EqualTo(before.target).Within(0.0001f));
                Assert.That(after.targetVelocity, Is.EqualTo(before.targetVelocity).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(result.Root);
            }
        }

        static MjcfRobotAsset Parse(string xml)
        {
            return MjcfParser.Parse(xml, "test", "memory", new MjcfImportSettings());
        }

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
