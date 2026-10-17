namespace DanmanAssetFinder.Services;

/// <summary>
/// Locates PNG previews that S&box generates under .sbox/thumbnails (mirrors Assets-relative paths).
/// </summary>
public static class SboxThumbnailLocator
{
    public static IReadOnlyList<string> DiscoverThumbnailRoots(string scanRoot, string? assetsRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in new[] { scanRoot, assetsRoot }.Where(p => !string.IsNullOrWhiteSpace(p)))
            CollectFromWalkUp(start!, roots);

        TryAddSboxInstallThumbnailRoots(scanRoot, assetsRoot, roots);

        return roots.ToList();
    }

    public static string? FindForGameAssetPath(string gameAssetPath, IReadOnlyList<string> thumbnailRoots)
    {
        if (string.IsNullOrWhiteSpace(gameAssetPath) || thumbnailRoots.Count == 0)
            return null;

        var normalized = gameAssetPath.Replace('\\', '/').TrimStart('/');
        var fileName = Path.GetFileName(normalized.Replace('/', Path.DirectorySeparatorChar));

        foreach (var root in thumbnailRoots)
        {
            foreach (var candidate in BuildCandidates(root, normalized, fileName))
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static readonly string[] PreviewExtensions = [".png", ".jpg", ".jpeg"];

    private static IEnumerable<string> BuildCandidates(string thumbnailRoot, string gamePath, string fileName)
    {
        foreach (var ext in PreviewExtensions)
        {
            yield return Path.Combine(thumbnailRoot, gamePath + ext);

            if (!string.IsNullOrEmpty(fileName))
                yield return Path.Combine(thumbnailRoot, fileName + ext);
        }

        if (gamePath.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            var cloudPath = ".cloud/" + gamePath;
            foreach (var ext in PreviewExtensions)
                yield return Path.Combine(thumbnailRoot, cloudPath + ext);
        }
    }

    private static void CollectFromWalkUp(string startPath, HashSet<string> roots)
    {
        var dir = Directory.Exists(startPath)
            ? new DirectoryInfo(Path.GetFullPath(startPath))
            : new DirectoryInfo(Path.GetFullPath(Path.GetDirectoryName(startPath) ?? startPath));

        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var thumb = Path.Combine(dir.FullName, ".sbox", "thumbnails");
            if (Directory.Exists(thumb))
                roots.Add(thumb);

            if (dir.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                var addonThumb = Path.Combine(dir.Parent?.FullName ?? dir.FullName, ".sbox", "thumbnails");
                if (Directory.Exists(addonThumb))
                    roots.Add(addonThumb);
            }
        }
    }

    private static void TryAddSboxInstallThumbnailRoots(string? scanRoot, string? assetsRoot, HashSet<string> roots)
    {
        foreach (var path in new[] { scanRoot, assetsRoot }.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var sboxRoot = FindSboxInstallRoot(path!);
            if (sboxRoot is null)
                continue;

            AddIfExists(Path.Combine(sboxRoot, ".sbox", "thumbnails"), roots);

            var addonsDir = Path.Combine(sboxRoot, "addons");
            if (!Directory.Exists(addonsDir))
                continue;

            foreach (var addon in Directory.EnumerateDirectories(addonsDir))
            {
                AddIfExists(Path.Combine(addon, ".sbox", "thumbnails"), roots);
            }
        }
    }

    private static string? FindSboxInstallRoot(string path)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(path));
        for (var i = 0; i < 16 && dir is not null; i++, dir = dir.Parent)
        {
            if (dir.Name.Equals("sbox", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(dir.FullName, "sbox.exe")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    private static void AddIfExists(string path, HashSet<string> roots)
    {
        if (Directory.Exists(path))
            roots.Add(path);
    }
}
