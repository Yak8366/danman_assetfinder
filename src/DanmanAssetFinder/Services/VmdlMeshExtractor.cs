using System.Text.RegularExpressions;

namespace DanmanAssetFinder.Services;

public static partial class VmdlMeshExtractor
{
    private static readonly string[] MeshExtensions = [".fbx", ".dmx", ".obj", ".gltf", ".glb"];

    public static string? ExtractPrimaryMeshGamePath(string vmdlPath)
    {
        if (!File.Exists(vmdlPath))
            return null;

        var text = File.ReadAllText(vmdlPath);

        foreach (Match match in RenderMeshFilePattern().Matches(text))
        {
            var path = match.Groups[1].Value.Trim();
            if (IsMeshPath(path))
                return path.Replace('\\', '/');
        }

        foreach (Match match in GenericFilenamePattern().Matches(text))
        {
            var path = match.Groups[1].Value.Trim();
            if (IsMeshPath(path))
                return path.Replace('\\', '/');
        }

        return null;
    }

    private static bool IsMeshPath(string path)
    {
        var ext = Path.GetExtension(path);
        return MeshExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"filename\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex GenericFilenamePattern();

    [GeneratedRegex(@"_class\s*=\s*""RenderMeshFile""[\s\S]*?filename\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RenderMeshFilePattern();
}
