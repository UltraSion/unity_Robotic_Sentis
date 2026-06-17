using Ne.MjcfImporter;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    public static class MjcfCoordinateSystem
    {
        public static Vector3 ToUnityVector(Vector3 mjcfVector, float scale = 1f)
        {
            return new Vector3(-mjcfVector.y, mjcfVector.z, mjcfVector.x) * scale;
        }

        public static Vector3 ToMjcfVector(Vector3 unityVector, float inverseScale = 1f)
        {
            return new Vector3(unityVector.z, -unityVector.x, unityVector.y) * inverseScale;
        }

        public static Vector3 ToUnitySize(Vector3 mjcfSize, float scale = 1f)
        {
            Vector3 converted = ToUnityVector(mjcfSize, scale);
            return new Vector3(Mathf.Abs(converted.x), Mathf.Abs(converted.y), Mathf.Abs(converted.z));
        }

        public static Quaternion ToUnityRotation(Quaternion mjcfRotation)
        {
            Vector3 right = ToUnityVector(mjcfRotation * ToMjcfVector(Vector3.right)).normalized;
            Vector3 up = ToUnityVector(mjcfRotation * ToMjcfVector(Vector3.up)).normalized;
            Vector3 forward = ToUnityVector(mjcfRotation * ToMjcfVector(Vector3.forward)).normalized;

            if (forward.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            Quaternion rotation = Quaternion.LookRotation(forward, up);
            Vector3 correctedRight = rotation * Vector3.right;
            if (Vector3.Dot(correctedRight, right) < 0f)
            {
                rotation = Quaternion.LookRotation(forward, -up);
            }

            return rotation;
        }

        public static Quaternion FromMjcfAxisAngle(Vector3 axis, float angle, MjcfAngleMode angleMode)
        {
            float degrees = angleMode == MjcfAngleMode.Radian ? angle * Mathf.Rad2Deg : angle;
            return Quaternion.AngleAxis(degrees, axis.normalized);
        }

        public static Quaternion FromMjcfEuler(Vector3 euler, MjcfAngleMode angleMode)
        {
            return FromMjcfEuler(euler, angleMode, "xyz");
        }

        public static Quaternion FromMjcfEuler(Vector3 euler, MjcfAngleMode angleMode, string sequence)
        {
            Vector3 degrees = angleMode == MjcfAngleMode.Radian ? euler * Mathf.Rad2Deg : euler;
            sequence = string.IsNullOrWhiteSpace(sequence) || sequence.Length != 3 ? "xyz" : sequence;
            Quaternion result = Quaternion.identity;
            foreach (char axisCode in sequence)
            {
                Quaternion step = Quaternion.AngleAxis(AxisAngle(degrees, axisCode), AxisVector(axisCode));
                result = char.IsUpper(axisCode) ? step * result : result * step;
            }

            return result;
        }

        public static Quaternion FromMjcfXyAxes(Vector3 xAxis, Vector3 yAxis)
        {
            Vector3 x = xAxis.sqrMagnitude > 0.0001f ? xAxis.normalized : Vector3.right;
            Vector3 y = yAxis.sqrMagnitude > 0.0001f ? yAxis.normalized : Vector3.up;
            y = (y - Vector3.Project(y, x)).normalized;
            if (y.sqrMagnitude < 0.0001f)
            {
                y = Vector3.up;
            }

            Vector3 z = Vector3.Cross(x, y).normalized;
            return Quaternion.LookRotation(z, y);
        }

        public static Quaternion FromMjcfZAxis(Vector3 zAxis)
        {
            if (zAxis.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.FromToRotation(Vector3.forward, zAxis.normalized);
        }

        static float AxisAngle(Vector3 euler, char axisCode)
        {
            return char.ToLowerInvariant(axisCode) switch
            {
                'x' => euler.x,
                'y' => euler.y,
                'z' => euler.z,
                _ => 0f
            };
        }

        static Vector3 AxisVector(char axisCode)
        {
            return char.ToLowerInvariant(axisCode) switch
            {
                'x' => Vector3.right,
                'y' => Vector3.up,
                'z' => Vector3.forward,
                _ => Vector3.forward
            };
        }

        public static Quaternion AxisToXDriveRotation(Vector3 unityAxis)
        {
            if (unityAxis.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.FromToRotation(Vector3.right, unityAxis.normalized);
        }

        public static void ApplyUnityCoordinates(MjcfRobotAsset asset)
        {
            float scale = Mathf.Max(0.0001f, asset.importSettings.scale);
            foreach (MjcfBody body in asset.rootBodies)
            {
                ApplyUnityCoordinates(body, scale);
            }
        }

        static void ApplyUnityCoordinates(MjcfBody body, float scale)
        {
            body.unityPosition = ToUnityVector(body.mjcfPosition, scale);
            body.unityRotation = ToUnityRotation(body.mjcfRotation);

            foreach (MjcfJoint joint in body.joints)
            {
                joint.unityPosition = ToUnityVector(joint.mjcfPosition, scale);
                joint.unityAxis = ToUnityVector(joint.mjcfAxis).normalized;
                if (joint.unityAxis.sqrMagnitude < 0.0001f)
                {
                    joint.unityAxis = Vector3.right;
                }
            }

            foreach (MjcfGeom geom in body.geoms)
            {
                ApplyUnityGeomCoordinates(geom, scale);
            }

            foreach (MjcfBody child in body.children)
            {
                ApplyUnityCoordinates(child, scale);
            }
        }

        static void ApplyUnityGeomCoordinates(MjcfGeom geom, float scale)
        {
            geom.unityRadius = SizeValue(geom, 0) * scale;
            geom.unityHalfExtents = ToUnitySize(geom.mjcfSize, scale);
            geom.unityLength = 0f;
            geom.unityCylinderLength = 0f;
            geom.unitySize = geom.unityHalfExtents;

            if (geom.hasFromTo && (geom.type == MjcfGeomType.Capsule || geom.type == MjcfGeomType.Cylinder))
            {
                geom.unityFrom = ToUnityVector(geom.mjcfFrom, scale);
                geom.unityTo = ToUnityVector(geom.mjcfTo, scale);
                geom.unityCenter = (geom.unityFrom + geom.unityTo) * 0.5f;
                geom.unityPosition = geom.unityCenter;
                Vector3 direction = geom.unityTo - geom.unityFrom;
                float segmentLength = direction.magnitude;
                geom.unityCylinderLength = segmentLength;
                geom.unityLength = geom.type == MjcfGeomType.Capsule
                    ? segmentLength + geom.unityRadius * 2f
                    : segmentLength;
                geom.unityRotation = ToUnityRotation(geom.mjcfRotation);
                geom.unityRotation = direction.sqrMagnitude > 0.0001f
                    ? Quaternion.FromToRotation(Vector3.up, direction.normalized)
                    : Quaternion.identity;
                geom.unitySize = new Vector3(geom.unityRadius, geom.unityLength * 0.5f, geom.unityRadius);
                return;
            }

            geom.unityCenter = ToUnityVector(geom.mjcfPosition, scale);
            geom.unityPosition = geom.unityCenter;
            geom.unityRotation = ToUnityRotation(geom.mjcfRotation);

            switch (geom.type)
            {
                case MjcfGeomType.Sphere:
                    geom.unityRadius = SizeValue(geom, 0) * scale;
                    geom.unitySize = Vector3.one * geom.unityRadius;
                    break;
                case MjcfGeomType.Capsule:
                {
                    float halfCylinder = SizeValue(geom, 1) * scale;
                    geom.unityCylinderLength = halfCylinder * 2f;
                    geom.unityLength = geom.unityCylinderLength + geom.unityRadius * 2f;
                    geom.unitySize = new Vector3(geom.unityRadius, geom.unityLength * 0.5f, geom.unityRadius);
                    break;
                }
                case MjcfGeomType.Cylinder:
                {
                    float halfHeight = SizeValue(geom, 1) * scale;
                    geom.unityLength = halfHeight * 2f;
                    geom.unityCylinderLength = geom.unityLength;
                    geom.unitySize = new Vector3(geom.unityRadius, geom.unityLength * 0.5f, geom.unityRadius);
                    break;
                }
                case MjcfGeomType.Box:
                    geom.unitySize = geom.unityHalfExtents;
                    break;
            }
        }

        static float SizeValue(MjcfGeom geom, int index)
        {
            if (geom.mjcfSizeValues != null && index >= 0 && index < geom.mjcfSizeValues.Count)
            {
                return Mathf.Max(0f, geom.mjcfSizeValues[index]);
            }

            return index switch
            {
                0 => Mathf.Max(0f, geom.mjcfSize.x),
                1 => Mathf.Max(0f, geom.mjcfSize.y),
                2 => Mathf.Max(0f, geom.mjcfSize.z),
                _ => 0f
            };
        }
    }
}
