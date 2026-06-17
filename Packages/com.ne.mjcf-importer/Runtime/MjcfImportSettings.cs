using System;
using UnityEngine;

namespace Ne.MjcfImporter
{
    [Serializable]
    public sealed class MjcfImportSettings
    {
        public bool generateVisualGeometry = true;
        public bool generateColliders = true;
        public bool applyJointLimits = true;
        public bool applyStiffnessDamping = true;
        public bool importMeshGeoms;
        public float scale = 1f;
        public float massScale = 1f;
        public float stiffnessScale = 1f;
        public float dampingScale = 1f;
        public float defaultDensity = 1000f;
        public MjcfCoordinatePolicy coordinatePolicy = MjcfCoordinatePolicy.ZUpRoboticsToUnity;

        public MjcfImportSettings Clone()
        {
            return (MjcfImportSettings)MemberwiseClone();
        }
    }

    public enum MjcfCoordinatePolicy
    {
        ZUpRoboticsToUnity = 0
    }
}
