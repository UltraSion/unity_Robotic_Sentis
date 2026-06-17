using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ne.MjcfImporter
{
    public sealed class MjcfActuatorController : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            public string actuatorName;
            public string jointName;
            public MjcfActuatorType type;
            public ArticulationBody articulationBody;
            public float gear = 1f;
            public bool ctrlLimited;
            public Vector2 ctrlRange;
        }

        public MjcfRobotAsset robotAsset;
        public List<Binding> bindings = new();

        public bool SetControl(string actuatorName, float value)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (string.Equals(bindings[i].actuatorName, actuatorName, StringComparison.Ordinal))
                {
                    ApplyControl(bindings[i], value);
                    return true;
                }
            }

            return false;
        }

        public bool SetControl(int index, float value)
        {
            if (index < 0 || index >= bindings.Count)
            {
                return false;
            }

            ApplyControl(bindings[index], value);
            return true;
        }

        void ApplyControl(Binding binding, float value)
        {
            if (binding == null || binding.articulationBody == null)
            {
                return;
            }

            float control = binding.ctrlLimited
                ? Mathf.Clamp(value, binding.ctrlRange.x, binding.ctrlRange.y)
                : value;
            float drivenValue = control * binding.gear;
            ArticulationDrive drive = binding.articulationBody.xDrive;

            switch (binding.type)
            {
                case MjcfActuatorType.Position:
                    drive.target = drivenValue;
                    binding.articulationBody.xDrive = drive;
                    break;
                case MjcfActuatorType.Velocity:
                    drive.targetVelocity = drivenValue;
                    binding.articulationBody.xDrive = drive;
                    break;
            }
        }
    }
}
