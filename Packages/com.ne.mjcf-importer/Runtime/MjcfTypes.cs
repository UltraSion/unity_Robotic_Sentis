using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ne.MjcfImporter
{
    public enum MjcfAngleMode
    {
        Degree,
        Radian
    }

    public enum MjcfJointType
    {
        Free,
        Hinge,
        Slide,
        Ball,
        Fixed
    }

    public enum MjcfGeomType
    {
        Box,
        Sphere,
        Capsule,
        Cylinder,
        Mesh,
        Unsupported
    }

    public enum MjcfActuatorType
    {
        Position,
        Velocity,
        Motor,
        Unsupported
    }

    [Serializable]
    public sealed class MjcfBody
    {
        public string name;
        public Vector3 mjcfPosition;
        public Quaternion mjcfRotation = Quaternion.identity;
        public Vector3 unityPosition;
        public Quaternion unityRotation = Quaternion.identity;
        public List<MjcfJoint> joints = new();
        public List<MjcfGeom> geoms = new();
        public List<MjcfBody> children = new();
    }

    [Serializable]
    public sealed class MjcfJoint
    {
        public string name;
        public MjcfJointType type = MjcfJointType.Hinge;
        public Vector3 mjcfPosition;
        public Vector3 unityPosition;
        public Vector3 mjcfAxis = Vector3.right;
        public Vector3 unityAxis = Vector3.right;
        public Vector2 range;
        public bool limited;
        public float stiffness;
        public float damping;
    }

    [Serializable]
    public sealed class MjcfGeom
    {
        public string name;
        public MjcfGeomType type = MjcfGeomType.Sphere;
        public Vector3 mjcfPosition;
        public Quaternion mjcfRotation = Quaternion.identity;
        public Vector3 mjcfSize = Vector3.one;
        public List<float> mjcfSizeValues = new();
        public bool hasFromTo;
        public Vector3 mjcfFrom;
        public Vector3 mjcfTo;
        public Vector3 unityPosition;
        public Vector3 unityCenter;
        public Quaternion unityRotation = Quaternion.identity;
        public Vector3 unitySize = Vector3.one;
        public Vector3 unityHalfExtents;
        public float unityRadius;
        public float unityLength;
        public float unityCylinderLength;
        public Vector3 unityFrom;
        public Vector3 unityTo;
        public float density;
        public float mass;
        public string meshName;
    }

    [Serializable]
    public sealed class MjcfActuator
    {
        public string name;
        public string jointName;
        public MjcfActuatorType type = MjcfActuatorType.Unsupported;
        public string rawType;
        public float gear = 1f;
        public Vector2 ctrlRange;
        public bool ctrlLimited;
        public float kp;
        public float kv;
    }
}
