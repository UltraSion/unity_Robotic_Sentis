using System.IO;
using Ne.MjcfImporter;
using UnityEditor;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    public static class MjcfXmlMenuImporter
    {
        const string GeneratedRoot = "Assets/MJCFGenerated";

        [MenuItem("Assets/NE/Import MJCF XML", true)]
        static bool ValidateImportXml()
        {
            foreach (Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (Path.GetExtension(path).Equals(".xml", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem("Assets/NE/Import MJCF XML")]
        static void ImportSelectedXml()
        {
            foreach (Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (Path.GetExtension(path).Equals(".xml", System.StringComparison.OrdinalIgnoreCase))
                {
                    ImportXmlAsset(path);
                }
            }
        }

        static void ImportXmlAsset(string assetPath)
        {
            var settings = new MjcfImportSettings();
            MjcfRobotAsset data = MjcfParser.ParseFile(assetPath, settings);
            ArticulationRobotBuildResult build = ArticulationRobotBuilder.Build(data);

            EnsureFolder(GeneratedRoot);
            string modelFolder = $"{GeneratedRoot}/{SanitizePath(Path.GetFileNameWithoutExtension(assetPath))}";
            EnsureFolder(modelFolder);

            string dataPath = AssetDatabase.GenerateUniqueAssetPath($"{modelFolder}/{data.name}.asset");
            AssetDatabase.CreateAsset(data, dataPath);

            foreach (Mesh mesh in build.Meshes)
            {
                string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{modelFolder}/{SanitizePath(mesh.name)}.asset");
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{modelFolder}/{SanitizePath(data.modelName)}.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(build.Root, prefabPath);
            Object.DestroyImmediate(build.Root);
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        static string SanitizePath(string value)
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
