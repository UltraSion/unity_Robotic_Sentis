using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Ne.MjcfImporter;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    public static class MjcfParser
    {
        sealed class ParseContext
        {
            public MjcfAngleMode AngleMode = MjcfAngleMode.Degree;
            public string EulerSequence = "xyz";
            public float DefaultDensity;
            public readonly Dictionary<string, MjcfDefaultClass> DefaultClasses = new(StringComparer.OrdinalIgnoreCase);
            public MjcfDefaultClass RootDefault;
        }

        sealed class MjcfDefaultClass
        {
            public string Name;
            public MjcfDefaultClass Parent;
            public readonly Dictionary<string, XElement> Elements = new(StringComparer.OrdinalIgnoreCase);

            public XElement FindElement(string localName)
            {
                return Elements.TryGetValue(localName, out XElement element)
                    ? element
                    : Parent?.FindElement(localName);
            }
        }

        public static MjcfRobotAsset ParseFile(string path, MjcfImportSettings settings)
        {
            return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), path, settings);
        }

        public static MjcfRobotAsset Parse(string xml, string fallbackModelName, string originalPath, MjcfImportSettings settings)
        {
            XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            XElement root = document.Root;
            if (root == null || root.Name.LocalName != "mujoco")
            {
                throw new InvalidDataException("MJCF root element must be <mujoco>.");
            }

            var context = new ParseContext
            {
                AngleMode = ParseAngleMode(root.Element("compiler")),
                EulerSequence = ParseEulerSequence(root.Element("compiler")),
                DefaultDensity = settings.defaultDensity
            };
            ReadDefaults(root.Elements().Where(child => child.Name.LocalName == "default"), context);

            var asset = ScriptableObject.CreateInstance<MjcfRobotAsset>();
            asset.name = $"{SanitizeName(Attribute(root, "model", fallbackModelName))}_MjcfData";
            asset.modelName = Attribute(root, "model", fallbackModelName);
            asset.originalPath = originalPath;
            asset.angleMode = context.AngleMode;
            asset.eulerSequence = context.EulerSequence;
            asset.importSettings = settings.Clone();

            XElement worldBody = root.Element("worldbody");
            if (worldBody == null)
            {
                throw new InvalidDataException("MJCF file does not contain a <worldbody> element.");
            }

            foreach (XElement bodyElement in Elements(worldBody, "body"))
            {
                asset.rootBodies.Add(ParseBody(bodyElement, context, string.Empty));
            }

            XElement actuatorElement = root.Element("actuator");
            if (actuatorElement != null)
            {
                foreach (XElement child in actuatorElement.Elements())
                {
                    asset.actuators.Add(ParseActuator(child, context, string.Empty));
                }
            }

            MjcfCoordinateSystem.ApplyUnityCoordinates(asset);
            return asset;
        }

        static void ReadDefaults(IEnumerable<XElement> defaultElements, ParseContext context)
        {
            var rootDefault = new MjcfDefaultClass
            {
                Name = string.Empty
            };
            context.RootDefault = rootDefault;
            context.DefaultClasses[string.Empty] = rootDefault;

            foreach (XElement defaultElement in defaultElements)
            {
                string name = Attribute(defaultElement, "class", null);
                if (string.IsNullOrEmpty(name))
                {
                    ReadDefaultInto(defaultElement, rootDefault, context);
                }
                else
                {
                    var defaultClass = new MjcfDefaultClass
                    {
                        Name = name,
                        Parent = rootDefault
                    };
                    context.DefaultClasses[name] = defaultClass;
                    ReadDefaultInto(defaultElement, defaultClass, context);
                }
            }
        }

        static void ReadDefaultInto(XElement defaultElement, MjcfDefaultClass defaultClass, ParseContext context)
        {
            foreach (XElement child in defaultElement.Elements())
            {
                string localName = child.Name.LocalName;
                if (localName == "default")
                {
                    string childName = Attribute(child, "class", null);
                    if (string.IsNullOrWhiteSpace(childName))
                    {
                        ReadDefaultInto(child, defaultClass, context);
                        continue;
                    }

                    var childDefault = new MjcfDefaultClass
                    {
                        Name = childName,
                        Parent = defaultClass
                    };
                    context.DefaultClasses[childName] = childDefault;
                    ReadDefaultInto(child, childDefault, context);
                    continue;
                }

                defaultClass.Elements[localName] = child;
                if (localName == "geom" && TryParseFloat(Attribute(child, "density", null), out float density))
                {
                    context.DefaultDensity = density;
                }
            }
        }

        static MjcfAngleMode ParseAngleMode(XElement compiler)
        {
            string angle = Attribute(compiler, "angle", "degree");
            return string.Equals(angle, "radian", StringComparison.OrdinalIgnoreCase)
                ? MjcfAngleMode.Radian
                : MjcfAngleMode.Degree;
        }

        static string ParseEulerSequence(XElement compiler)
        {
            string sequence = Attribute(compiler, "eulerseq", "xyz");
            return string.IsNullOrWhiteSpace(sequence) || sequence.Length != 3 ? "xyz" : sequence;
        }

        static MjcfBody ParseBody(XElement element, ParseContext context, string inheritedDefaultClass)
        {
            string childDefaultClass = Attribute(element, "childclass", inheritedDefaultClass);
            var body = new MjcfBody
            {
                name = Attribute(element, "name", "body"),
                mjcfPosition = ParseVector3(Attribute(element, "pos", null), Vector3.zero),
                mjcfRotation = ParseRotation(element, null, context)
            };

            foreach (XElement jointElement in Elements(element, "joint"))
            {
                body.joints.Add(ParseJoint(jointElement, context, childDefaultClass));
            }

            foreach (XElement freeJointElement in Elements(element, "freejoint"))
            {
                body.joints.Add(new MjcfJoint
                {
                    name = Attribute(freeJointElement, "name", "freejoint"),
                    type = MjcfJointType.Free,
                    limited = false
                });
            }

            foreach (XElement geomElement in Elements(element, "geom"))
            {
                body.geoms.Add(ParseGeom(geomElement, context, childDefaultClass));
            }

            foreach (XElement childElement in Elements(element, "body"))
            {
                body.children.Add(ParseBody(childElement, context, childDefaultClass));
            }

            return body;
        }

        static MjcfJoint ParseJoint(XElement element, ParseContext context, string defaultClassName)
        {
            string effectiveClass = Attribute(element, "class", defaultClassName);
            XElement defaults = DefaultElement(context, effectiveClass, "joint");
            string type = Attribute(element, "type", Attribute(defaults, "type", "hinge")).ToLowerInvariant();
            var joint = new MjcfJoint
            {
                name = Attribute(element, "name", type),
                type = type switch
                {
                    "free" => MjcfJointType.Free,
                    "hinge" => MjcfJointType.Hinge,
                    "slide" => MjcfJointType.Slide,
                    "ball" => MjcfJointType.Ball,
                    _ => MjcfJointType.Fixed
                },
                mjcfPosition = ParseVector3(Attribute(element, "pos", Attribute(defaults, "pos", null)), Vector3.zero),
                mjcfAxis = ParseVector3(Attribute(element, "axis", Attribute(defaults, "axis", null)), Vector3.right),
                limited = ParseBool(Attribute(element, "limited", Attribute(defaults, "limited", "false"))),
                range = ParseRange(Attribute(element, "range", Attribute(defaults, "range", null)), context.AngleMode),
                stiffness = ParseFloat(Attribute(element, "stiffness", Attribute(defaults, "stiffness", "0"))),
                damping = ParseFloat(Attribute(element, "damping", Attribute(defaults, "damping", "0")))
            };

            if (Attribute(element, "range", null) != null)
            {
                joint.limited = true;
            }

            return joint;
        }

        static MjcfGeom ParseGeom(XElement element, ParseContext context, string defaultClassName)
        {
            string effectiveClass = Attribute(element, "class", defaultClassName);
            XElement defaults = DefaultElement(context, effectiveClass, "geom");
            string type = Attribute(element, "type", Attribute(defaults, "type", "sphere")).ToLowerInvariant();
            string size = Attribute(element, "size", Attribute(defaults, "size", null));
            float[] sizeValues = ParseFloatArray(size);
            var geom = new MjcfGeom
            {
                name = Attribute(element, "name", type),
                type = type switch
                {
                    "box" => MjcfGeomType.Box,
                    "sphere" => MjcfGeomType.Sphere,
                    "capsule" => MjcfGeomType.Capsule,
                    "cylinder" => MjcfGeomType.Cylinder,
                    "mesh" => MjcfGeomType.Mesh,
                    _ => MjcfGeomType.Unsupported
                },
                mjcfPosition = ParseVector3(Attribute(element, "pos", Attribute(defaults, "pos", null)), Vector3.zero),
                mjcfRotation = ParseRotation(element, defaults, context),
                mjcfSize = ParseSize(size),
                mjcfSizeValues = sizeValues.ToList(),
                density = ParseFloat(Attribute(element, "density", Attribute(defaults, "density", context.DefaultDensity.ToString(CultureInfo.InvariantCulture)))),
                mass = ParseFloat(Attribute(element, "mass", Attribute(defaults, "mass", "0"))),
                meshName = Attribute(element, "mesh", Attribute(defaults, "mesh", null))
            };

            string fromTo = Attribute(element, "fromto", Attribute(defaults, "fromto", null));
            if (!string.IsNullOrWhiteSpace(fromTo))
            {
                float[] values = ParseFloatArray(fromTo);
                if (values.Length >= 6)
                {
                    geom.hasFromTo = true;
                    geom.mjcfFrom = new Vector3(values[0], values[1], values[2]);
                    geom.mjcfTo = new Vector3(values[3], values[4], values[5]);
                }
            }

            return geom;
        }

        static MjcfActuator ParseActuator(XElement element, ParseContext context, string defaultClassName, bool isDefault = false)
        {
            string rawType = element.Name.LocalName;
            string effectiveClass = Attribute(element, "class", defaultClassName);
            XElement defaults = DefaultElement(context, effectiveClass, rawType);

            var actuator = new MjcfActuator
            {
                name = Attribute(element, "name", isDefault ? rawType : Attribute(element, "joint", rawType)),
                jointName = Attribute(element, "joint", Attribute(defaults, "joint", null)),
                rawType = rawType,
                type = rawType.ToLowerInvariant() switch
                {
                    "position" => MjcfActuatorType.Position,
                    "velocity" => MjcfActuatorType.Velocity,
                    "motor" => MjcfActuatorType.Motor,
                    _ => MjcfActuatorType.Unsupported
                },
                gear = ParseFirstFloat(Attribute(element, "gear", Attribute(defaults, "gear", "1"))),
                ctrlLimited = ParseBool(Attribute(element, "ctrllimited", Attribute(defaults, "ctrllimited", "false"))),
                ctrlRange = ParseVector2(Attribute(element, "ctrlrange", Attribute(defaults, "ctrlrange", null)), Vector2.zero),
                kp = ParseFloat(Attribute(element, "kp", Attribute(defaults, "kp", "0"))),
                kv = ParseFloat(Attribute(element, "kv", Attribute(defaults, "kv", "0")))
            };

            return actuator;
        }

        static XElement DefaultElement(ParseContext context, string className, string localName)
        {
            if (!string.IsNullOrWhiteSpace(className) &&
                context.DefaultClasses.TryGetValue(className, out MjcfDefaultClass defaultClass))
            {
                return defaultClass.FindElement(localName);
            }

            return context.RootDefault?.FindElement(localName);
        }

        static Quaternion ParseRotation(XElement element, XElement defaults, ParseContext context)
        {
            string quat = Attribute(element, "quat", Attribute(defaults, "quat", null));
            if (!string.IsNullOrWhiteSpace(quat))
            {
                float[] values = ParseFloatArray(quat);
                if (values.Length >= 4)
                {
                    return Normalize(new Quaternion(values[1], values[2], values[3], values[0]));
                }
            }

            string axisAngle = Attribute(element, "axisangle", Attribute(defaults, "axisangle", null));
            if (!string.IsNullOrWhiteSpace(axisAngle))
            {
                float[] values = ParseFloatArray(axisAngle);
                if (values.Length >= 4)
                {
                    return MjcfCoordinateSystem.FromMjcfAxisAngle(new Vector3(values[0], values[1], values[2]), values[3], context.AngleMode);
                }
            }

            string xyAxes = Attribute(element, "xyaxes", Attribute(defaults, "xyaxes", null));
            if (!string.IsNullOrWhiteSpace(xyAxes))
            {
                float[] values = ParseFloatArray(xyAxes);
                if (values.Length >= 6)
                {
                    return MjcfCoordinateSystem.FromMjcfXyAxes(
                        new Vector3(values[0], values[1], values[2]),
                        new Vector3(values[3], values[4], values[5]));
                }
            }

            string zAxis = Attribute(element, "zaxis", Attribute(defaults, "zaxis", null));
            if (!string.IsNullOrWhiteSpace(zAxis))
            {
                return MjcfCoordinateSystem.FromMjcfZAxis(ParseVector3(zAxis, Vector3.forward));
            }

            string euler = Attribute(element, "euler", Attribute(defaults, "euler", null));
            if (!string.IsNullOrWhiteSpace(euler))
            {
                return MjcfCoordinateSystem.FromMjcfEuler(ParseVector3(euler, Vector3.zero), context.AngleMode, context.EulerSequence);
            }

            return Quaternion.identity;
        }

        static Quaternion Normalize(Quaternion quaternion)
        {
            float magnitude = Mathf.Sqrt(
                quaternion.x * quaternion.x +
                quaternion.y * quaternion.y +
                quaternion.z * quaternion.z +
                quaternion.w * quaternion.w);
            return magnitude > 0.0001f
                ? new Quaternion(quaternion.x / magnitude, quaternion.y / magnitude, quaternion.z / magnitude, quaternion.w / magnitude)
                : Quaternion.identity;
        }

        static IEnumerable<XElement> Elements(XElement element, string localName)
        {
            return element.Elements().Where(child => child.Name.LocalName == localName);
        }

        static string Attribute(XElement element, string name, string fallback)
        {
            return element?.Attribute(name)?.Value ?? fallback;
        }

        static Vector2 ParseRange(string value, MjcfAngleMode angleMode)
        {
            float[] values = ParseFloatArray(value);
            if (values.Length < 2)
            {
                return Vector2.zero;
            }

            Vector2 range = new(values[0], values[1]);
            return angleMode == MjcfAngleMode.Radian ? range * Mathf.Rad2Deg : range;
        }

        static Vector2 ParseVector2(string value, Vector2 fallback)
        {
            float[] values = ParseFloatArray(value);
            return values.Length >= 2 ? new Vector2(values[0], values[1]) : fallback;
        }

        static Vector3 ParseSize(string value)
        {
            float[] values = ParseFloatArray(value);
            return values.Length switch
            {
                0 => Vector3.one,
                1 => new Vector3(values[0], values[0], values[0]),
                2 => new Vector3(values[0], values[1], values[1]),
                _ => new Vector3(values[0], values[1], values[2])
            };
        }

        static Vector3 ParseVector3(string value, Vector3 fallback)
        {
            float[] values = ParseFloatArray(value);
            return values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : fallback;
        }

        static float[] ParseFloatArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<float>();
            }

            string[] parts = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<float>(parts.Length);
            foreach (string part in parts)
            {
                if (TryParseFloat(part, out float parsed))
                {
                    values.Add(parsed);
                }
            }

            return values.ToArray();
        }

        static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        static float ParseFloat(string value)
        {
            return TryParseFloat(value, out float parsed) ? parsed : 0f;
        }

        static float ParseFirstFloat(string value)
        {
            float[] values = ParseFloatArray(value);
            return values.Length > 0 ? values[0] : 0f;
        }

        static bool ParseBool(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "MjcfRobot";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}
