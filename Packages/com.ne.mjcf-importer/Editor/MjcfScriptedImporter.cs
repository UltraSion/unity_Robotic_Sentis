using Ne.MjcfImporter;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Ne.MjcfImporter.Editor
{
    [ScriptedImporter(1, "mjcf")]
    public sealed class MjcfScriptedImporter : ScriptedImporter
    {
        public MjcfImportSettings settings = new();

        public override void OnImportAsset(AssetImportContext ctx)
        {
            MjcfRobotAsset data = MjcfParser.ParseFile(ctx.assetPath, settings);
            ArticulationRobotBuildResult build = ArticulationRobotBuilder.Build(data);

            ctx.AddObjectToAsset("main", build.Root);
            ctx.SetMainObject(build.Root);
            ctx.AddObjectToAsset("data", data);

            for (int i = 0; i < build.Meshes.Count; i++)
            {
                Mesh mesh = build.Meshes[i];
                ctx.AddObjectToAsset($"mesh_{i}_{mesh.name}", mesh);
            }
        }
    }
}
