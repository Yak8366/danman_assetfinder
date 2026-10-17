using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DanmanAssetFinder.Models;

namespace DanmanAssetFinder.Services;

/// <summary>Loads thumbnails off the UI thread; only for the current page; can unload off-page images.</summary>
public static class ThumbnailLoader
{
    private static readonly ConcurrentDictionary<string, byte> Queued = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(3);

    public static void Queue(AssetEntry asset)
    {
        if (asset.IsSkeleton || asset.ThumbnailImage is not null)
            return;

        var source = asset.ThumbnailPath;
        if (string.IsNullOrWhiteSpace(source))
            return;

        var key = asset.FavoriteKey + "|" + source;
        if (!Queued.TryAdd(key, 0))
            return;

        var epoch = asset.ThumbnailEpoch;
        asset.IsLoadingThumbnail = true;

        _ = Task.Run(() => LoadAsync(asset, source, key, epoch));
    }

    public static void QueueMany(IEnumerable<AssetEntry> assets)
    {
        foreach (var asset in assets)
            Queue(asset);
    }

    /// <summary>Release decoded bitmaps so the GC can reclaim RAM when leaving a page.</summary>
    public static void Unload(AssetEntry asset)
    {
        asset.ThumbnailEpoch++;
        asset.ThumbnailImage = null;
        asset.IsLoadingThumbnail = false;
    }

    private static async Task LoadAsync(AssetEntry asset, string source, string key, int epoch)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (asset.ThumbnailEpoch != epoch)
                return;

            BitmapImage? image = null;

            var isRemote = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            if (isRemote)
            {
                var local = await CommunityThumbnailCache.GetLocalPathAsync(source).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(local))
                    image = CommunityThumbnailCache.LoadBitmapFromFile(local);
            }
            else if (File.Exists(source))
            {
                image = CommunityThumbnailCache.LoadBitmapFromFile(source);
            }

            if (asset.ThumbnailEpoch != epoch)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            await dispatcher.InvokeAsync(() =>
            {
                if (asset.ThumbnailEpoch != epoch)
                    return;

                asset.ThumbnailImage = image;
                asset.IsLoadingThumbnail = false;
            }, DispatcherPriority.Background);
        }
        catch
        {
            if (asset.ThumbnailEpoch != epoch)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (asset.ThumbnailEpoch == epoch)
                        asset.IsLoadingThumbnail = false;
                }, DispatcherPriority.Background);
            }
        }
        finally
        {
            Gate.Release();
            Queued.TryRemove(key, out _);
        }
    }
}
