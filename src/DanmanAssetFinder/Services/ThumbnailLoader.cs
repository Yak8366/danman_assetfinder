using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DanmanAssetFinder.Models;

namespace DanmanAssetFinder.Services;

/// <summary>Loads local/remote thumbnails off the UI thread and assigns BitmapImages asynchronously.</summary>
public static class ThumbnailLoader
{
    private static readonly ConcurrentDictionary<string, byte> Queued = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(6);

    public static void Queue(AssetEntry asset)
    {
        if (asset.IsSkeleton || asset.ThumbnailImage is not null)
            return;

        var source = asset.ThumbnailPath;
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (!Queued.TryAdd(asset.FavoriteKey + "|" + source, 0))
            return;

        var isRemote = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        // Only remote thumbs show skeleton pulse; local files load quietly
        if (isRemote)
            asset.IsLoadingThumbnail = true;

        _ = Task.Run(() => LoadAsync(asset, source));
    }

    public static void QueueMany(IEnumerable<AssetEntry> assets)
    {
        foreach (var asset in assets)
            Queue(asset);
    }

    private static async Task LoadAsync(AssetEntry asset, string source)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
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

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
                return;

            await dispatcher.InvokeAsync(() =>
            {
                asset.ThumbnailImage = image;
                asset.IsLoadingThumbnail = false;
                if (!string.IsNullOrWhiteSpace(source) && image is not null
                    && (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    // Keep remote URL on ThumbnailPath for preview/cache; local file optional
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                await dispatcher.InvokeAsync(() => asset.IsLoadingThumbnail = false, DispatcherPriority.Background);
            }
        }
        finally
        {
            Gate.Release();
            Queued.TryRemove(asset.FavoriteKey + "|" + source, out _);
        }
    }
}
