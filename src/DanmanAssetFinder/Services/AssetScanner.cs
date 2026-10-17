using DanmanAssetFinder.Models;

namespace DanmanAssetFinder.Services;

public static class AssetScanner
{
    public static IReadOnlyList<AssetEntry> Scan(string rootFolder, AssetCategory category)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            return [];

        var assetsRoot = AssetPathHelper.FindAssetsRoot(rootFolder) ?? rootFolder;
        var extensions = category.FileExtensions()
            .Select(e => e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var thumbnailRoots = SboxThumbnailLocator.DiscoverThumbnailRoots(rootFolder, assetsRoot);
        var results = new List<AssetEntry>();

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!extensions.Contains(ext))
                continue;

            results.Add(BuildEntry(file, category, assetsRoot, thumbnailRoots));
        }

        return results
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AssetEntry BuildEntry(
        string file,
        AssetCategory category,
        string assetsRoot,
        IReadOnlyList<string> thumbnailRoots)
    {
        var gamePath = AssetPathHelper.ToGamePath(assetsRoot, file);
        var meta = GameResourceParser.Parse(file);
        var displayName = meta.Title ?? Path.GetFileNameWithoutExtension(file);

        string? linkedModel = null;
        string? previewMesh = null;

        if (category is AssetCategory.Clothes or AssetCategory.Props or AssetCategory.Entities)
        {
            linkedModel = meta.Model;
            if (string.IsNullOrWhiteSpace(linkedModel) && file.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                linkedModel = gamePath;

            if (!string.IsNullOrWhiteSpace(linkedModel))
            {
                var vmdlAbsolute = AssetPathHelper.ResolveGamePathToAbsolute(assetsRoot, linkedModel)
                    ?? (file.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) ? file : null);

                if (vmdlAbsolute is not null)
                {
                    var meshGame = VmdlMeshExtractor.ExtractPrimaryMeshGamePath(vmdlAbsolute);
                    if (!string.IsNullOrWhiteSpace(meshGame))
                    {
                        previewMesh = AssetPathHelper.ResolveMeshBesideAsset(vmdlAbsolute, meshGame, assetsRoot)
                            ?? AssetPathHelper.ResolveGamePathToAbsolute(assetsRoot, meshGame);
                    }
                }
            }
        }

        var thumbnail = ThumbnailResolver.FindForAsset(
            file,
            gamePath,
            meta,
            assetsRoot,
            thumbnailRoots,
            linkedModel);

        return new AssetEntry
        {
            DisplayName = displayName,
            GamePath = gamePath,
            FullPath = file,
            Category = category,
            Subtitle = meta.Description ?? linkedModel,
            ThumbnailPath = thumbnail,
            PreviewMeshPath = previewMesh,
            LinkedModelGamePath = linkedModel
        };
    }
}
