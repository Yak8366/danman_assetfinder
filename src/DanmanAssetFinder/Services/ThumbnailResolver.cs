namespace DanmanAssetFinder.Services;

public static class ThumbnailResolver
{
    private static readonly string[] PreviewExtensions = [".png", ".jpg", ".jpeg"];

    private static readonly string[] LegacyImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public static string? FindForAsset(
        string assetFullPath,
        string gamePath,
        ParsedResource meta,
        string? assetsRoot,
        IReadOnlyList<string> sboxThumbnailRoots,
        string? linkedModelGamePath)
    {
        if (assetFullPath.EndsWith(".clothing", StringComparison.OrdinalIgnoreCase))
        {
            var clothingPreview = FindClothingPreviewBeside(assetFullPath);
            if (clothingPreview is not null)
                return clothingPreview;
        }

        var besideAsset = FindSboxPreviewBeside(assetFullPath);
        if (besideAsset is not null)
            return besideAsset;

        var cachedAsset = SboxThumbnailLocator.FindForGameAssetPath(gamePath, sboxThumbnailRoots);
        if (cachedAsset is not null)
            return cachedAsset;

        if (!string.IsNullOrWhiteSpace(meta.Icon))
        {
            var fromMeta = ResolveIconPath(assetFullPath, meta.Icon, assetsRoot);
            if (fromMeta is not null)
                return fromMeta;
        }

        foreach (var modelPath in ModelPathsToTry(gamePath, linkedModelGamePath, assetFullPath))
        {
            var cachedModel = SboxThumbnailLocator.FindForGameAssetPath(modelPath, sboxThumbnailRoots);
            if (cachedModel is not null)
                return cachedModel;

            if (assetsRoot is not null)
            {
                var vmdlFile = AssetPathHelper.ResolveGamePathToAbsolute(assetsRoot, modelPath);
                if (vmdlFile is not null)
                {
                    var besideVmdl = FindSboxPreviewBeside(vmdlFile) ?? FindLegacySiblingImage(vmdlFile);
                    if (besideVmdl is not null)
                        return besideVmdl;
                }
            }
        }

        return FindLegacySiblingImage(assetFullPath);
    }

    /// <summary>
    /// Clothing previews: {name}.clothing.png / .jpg / .jpeg (e.g. blue_sneakers.clothing.png).
    /// </summary>
    public static string? FindClothingPreviewBeside(string clothingAssetFullPath)
    {
        var dir = Path.GetDirectoryName(clothingAssetFullPath);
        var clothingName = Path.GetFileNameWithoutExtension(clothingAssetFullPath);
        if (dir is null || string.IsNullOrEmpty(clothingName))
            return null;

        return FindFirstExisting(dir, PreviewExtensions, ext => clothingName + ".clothing" + ext);
    }

    /// <summary>
    /// Other assets: {fullFileName}.png / .jpg / .jpeg (e.g. crate.vmdl.png).
    /// </summary>
    public static string? FindSboxPreviewBeside(string assetFullPath)
    {
        var dir = Path.GetDirectoryName(assetFullPath);
        var fileName = Path.GetFileName(assetFullPath);
        if (dir is null || string.IsNullOrEmpty(fileName))
            return null;

        return FindFirstExisting(dir, PreviewExtensions, ext => fileName + ext);
    }

    private static string? FindFirstExisting(
        string dir,
        IReadOnlyList<string> extensions,
        Func<string, string> buildFileName)
    {
        foreach (var ext in extensions)
        {
            var path = Path.Combine(dir, buildFileName(ext));
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string> ModelPathsToTry(
        string gamePath,
        string? linkedModelGamePath,
        string assetFullPath)
    {
        if (!string.IsNullOrWhiteSpace(linkedModelGamePath))
            yield return linkedModelGamePath.Replace('\\', '/');

        if (assetFullPath.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            yield return gamePath.Replace('\\', '/');
    }

    private static string? FindLegacySiblingImage(string assetFullPath)
    {
        var dir = Path.GetDirectoryName(assetFullPath);
        var baseName = Path.GetFileNameWithoutExtension(assetFullPath);
        if (dir is null || string.IsNullOrEmpty(baseName))
            return null;

        foreach (var ext in LegacyImageExtensions)
        {
            var direct = Path.Combine(dir, baseName + ext);
            if (File.Exists(direct))
                return direct;
        }

        foreach (var ext in LegacyImageExtensions)
        {
            var iconNamed = Path.Combine(dir, baseName + "_icon" + ext);
            if (File.Exists(iconNamed))
                return iconNamed;
        }

        return null;
    }

    private static string? ResolveIconPath(string assetFullPath, string iconValue, string? assetsRoot)
    {
        if (File.Exists(iconValue))
            return iconValue;

        if (!string.IsNullOrWhiteSpace(assetsRoot))
        {
            var fromRoot = AssetPathHelper.ResolveGamePathToAbsolute(assetsRoot, iconValue);
            if (fromRoot is not null)
                return fromRoot;
        }

        var dir = Path.GetDirectoryName(assetFullPath);
        if (dir is null)
            return null;

        var fileName = Path.GetFileName(iconValue.Replace('/', Path.DirectorySeparatorChar));
        var sibling = Path.Combine(dir, fileName);
        return File.Exists(sibling) ? sibling : null;
    }
}
