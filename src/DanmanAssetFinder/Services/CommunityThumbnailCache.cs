using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace DanmanAssetFinder.Services;

public static class CommunityThumbnailCache
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly object Gate = new();

    private static string CacheRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DanmanAssetFinder",
            "thumb-cache");

    public static async Task<string?> GetLocalPathAsync(string? remoteUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;

        if (!remoteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !remoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(remoteUrl) ? remoteUrl : null;
        }

        Directory.CreateDirectory(CacheRoot);
        var cachePath = Path.Combine(CacheRoot, Hash(remoteUrl) + ".png");
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            return cachePath;

        try
        {
            using var response = await Http.GetAsync(remoteUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct).ConfigureAwait(false);
            var bytes = memory.ToArray();

            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null)
                return null;

            lock (Gate)
            {
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                using var file = File.Create(cachePath);
                data.SaveTo(file);
            }

            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    public static BitmapImage? LoadBitmapFromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DanmanAssetFinder/1.0");
        return client;
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
