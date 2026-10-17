namespace DanmanAssetFinder.Services;

public static class AssetPathHelper
{
    public static string? FindAssetsRoot(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        var dir = new DirectoryInfo(Path.GetFullPath(folderPath));
        while (dir is not null)
        {
            if (dir.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }

    public static string ToGamePath(string assetsRoot, string fullFilePath)
    {
        var relative = Path.GetRelativePath(assetsRoot, fullFilePath);
        return relative.Replace('\\', '/');
    }

    public static string? ResolveGamePathToAbsolute(string assetsRoot, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return null;

        var normalized = gamePath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(assetsRoot, normalized));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }

    public static string? ResolveMeshBesideAsset(string assetFullPath, string meshGamePath, string? assetsRoot)
    {
        if (!string.IsNullOrWhiteSpace(assetsRoot))
        {
            var fromRoot = ResolveGamePathToAbsolute(assetsRoot, meshGamePath);
            if (fromRoot is not null)
                return fromRoot;
        }

        var fileName = Path.GetFileName(meshGamePath.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(assetFullPath);
        if (dir is null || string.IsNullOrEmpty(fileName))
            return null;

        var sibling = Path.Combine(dir, fileName);
        return File.Exists(sibling) ? sibling : null;
    }
}
