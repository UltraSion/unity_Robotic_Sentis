using System.Collections.Generic;
using UnityEngine;

namespace Ne.MjcfImporter
{
    public sealed class MjcfRobotAsset : ScriptableObject
    {
        public string modelName;
        public string originalPath;
        public MjcfAngleMode angleMode = MjcfAngleMode.Degree;
        public string eulerSequence = "xyz";
        public MjcfImportSettings importSettings = new();
        public List<MjcfBody> rootBodies = new();
        public List<MjcfActuator> actuators = new();
    }
}
