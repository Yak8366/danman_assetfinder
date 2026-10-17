using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DanmanAssetFinder.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DanmanAssetFinder.Services;

public static class CommunityClothingService
{
    private const string ClothingUrl = "https://sbox.game/ugc/clothing";

    private const string ExtractListScript = """
        (() => {
          const cards = [];
          const seen = new Set();
          for (const a of document.querySelectorAll('a[href]')) {
            const href = a.getAttribute('href') || '';
            const m = href.match(/^\/([a-z0-9_-]+)\/([a-z0-9_-]+)\/?$/i);
            if (!m) continue;
            const org = m[1], name = m[2], ident = org + '/' + name;
            if (seen.has(ident)) continue;
            let thumb = null;
            for (const img of a.querySelectorAll('img')) {
              const s = img.currentSrc || img.src || '';
              if (!s) continue;
              if (s.includes('/screenshots/') && (s.endsWith('.jpg') || s.endsWith('.jpeg') || s.endsWith('.png'))) {
                thumb = s; break;
              }
              if (!thumb && s.includes('package-thumbnails')) thumb = s;
              if (!thumb && (s.endsWith('.jpg') || s.endsWith('.jpeg') || s.endsWith('.png') || s.endsWith('.webp'))) thumb = s;
            }
            if (!thumb) continue;
            let title = name.replace(/_/g, ' ');
            const titleLink = [...document.querySelectorAll('a[href="/' + ident + '"]')]
              .map(x => (x.textContent || '').trim())
              .find(t => t && t !== ident && !t.includes('/') && t.length > 1);
            if (titleLink) title = titleLink;
            seen.add(ident);
            cards.push({ org, name, ident, title, thumb, pageUrl: 'https://sbox.game/' + ident });
          }
          return JSON.stringify(cards);
        })()
        """;

    private const string ExtractScreenshotScript = """
        (() => {
          const urls = [];
          const push = (u) => {
            if (!u) return;
            if (u.includes('cdn.sbox.game/org/') && u.includes('/screenshots/') &&
                (u.includes('.jpg') || u.includes('.jpeg') || u.includes('.png')))
              urls.push(u.split('?')[0]);
          };
          for (const img of document.querySelectorAll('img')) push(img.currentSrc || img.src);
          for (const e of performance.getEntriesByType('resource')) push(e.name);
          return JSON.stringify([...new Set(urls)]);
        })()
        """;

    public static async Task<IReadOnlyList<AssetEntry>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.Dispatcher is null)
            return [];

        // Background priority keeps the shell responsive while WebView warms up
        return await Application.Current.Dispatcher.InvokeAsync(
            () => FetchOnUiThreadAsync(cancellationToken),
            DispatcherPriority.Background).Task.Unwrap();
    }

    /// <summary>
    /// Upgrades community thumbs to workshop screenshot JPGs
    /// (e.g. https://cdn.sbox.game/org/igorr/swat_pants/screenshots/....jpg).
    /// </summary>
    public static async Task EnrichScreenshotsAsync(
        IReadOnlyList<AssetEntry> assets,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.Dispatcher is null || assets.Count == 0)
            return;

        await Application.Current.Dispatcher.InvokeAsync(
            () => EnrichScreenshotsOnUiThreadAsync(assets, progress, cancellationToken),
            DispatcherPriority.Background).Task.Unwrap();
    }

    private static async Task<IReadOnlyList<AssetEntry>> FetchOnUiThreadAsync(CancellationToken cancellationToken)
    {
        var host = new Window
        {
            Width = 1280,
            Height = 900,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Community scrape"
        };

        var webView = new WebView2();
        host.Content = webView;
        host.Show();

        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            cancellationToken.ThrowIfCancellationRequested();

            var packages = await WaitForPackageListAsync(webView, cancellationToken);
            if (packages.Count == 0)
                return [];

            // Return remote URLs immediately — ThumbnailLoader caches/decodes off the UI thread
            return packages.Select(ToAsset).ToList();
        }
        finally
        {
            host.Close();
            webView.Dispose();
        }
    }

    private static async Task EnrichScreenshotsOnUiThreadAsync(
        IReadOnlyList<AssetEntry> assets,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var community = assets.Where(a => a.IsCommunity && !string.IsNullOrWhiteSpace(a.CommunityUrl)).ToList();
        if (community.Count == 0)
            return;

        var host = new Window
        {
            Width = 1100,
            Height = 800,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Screenshot enrich"
        };

        var webView = new WebView2();
        host.Content = webView;
        host.Show();

        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);

            foreach (var asset in community)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Screenshot: {asset.DisplayName}");

                try
                {
                    var parts = (asset.CommunityIdent ?? asset.GamePath).Split('/', 2);
                    if (parts.Length != 2)
                        continue;

                    var org = parts[0];
                    var name = parts[1];

                    var nav = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs args) => nav.TrySetResult();
                    webView.NavigationCompleted += OnNav;
                    webView.CoreWebView2.Navigate(asset.CommunityUrl!);
                    await nav.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                    webView.NavigationCompleted -= OnNav;

                    await Task.Delay(850, cancellationToken);

                    var shots = await ExecuteJsonAsync<List<string>>(webView, ExtractScreenshotScript);
                    var best = shots?.FirstOrDefault(u =>
                        u.Contains($"/org/{org}/{name}/screenshots/", StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrWhiteSpace(best))
                        continue;

                    var local = await CommunityThumbnailCache.GetLocalPathAsync(best, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(local))
                        asset.ThumbnailPath = local;
                }
                catch
                {
                    // Keep existing cached thumb
                }
            }
        }
        finally
        {
            host.Close();
            webView.Dispose();
        }
    }

    private static async Task<List<CommunityPackageDto>> WaitForPackageListAsync(
        WebView2 webView,
        CancellationToken cancellationToken)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
                loaded.TrySetResult();
            else
                loaded.TrySetException(new InvalidOperationException("Failed to load sbox.game clothing page."));
        }

        webView.NavigationCompleted += OnNav;
        try
        {
            webView.CoreWebView2.Navigate(ClothingUrl);
            await loaded.Task.WaitAsync(cancellationToken);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(750, cancellationToken);

                var packages = await ExecuteJsonAsync<List<CommunityPackageDto>>(webView, ExtractListScript);
                if (packages is { Count: > 0 })
                    return packages;
            }
        }
        finally
        {
            webView.NavigationCompleted -= OnNav;
        }

        return [];
    }

    private static async Task<T?> ExecuteJsonAsync<T>(WebView2 webView, string script)
    {
        var json = await webView.ExecuteScriptAsync(script);
        var unquoted = JsonSerializer.Deserialize<string>(json) ?? "null";
        return JsonSerializer.Deserialize<T>(
            unquoted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static AssetEntry ToAsset(CommunityPackageDto package)
    {
        var title = string.IsNullOrWhiteSpace(package.Title)
            ? package.Name.Replace('_', ' ')
            : package.Title;

        return new AssetEntry
        {
            DisplayName = title,
            GamePath = package.Ident,
            FullPath = package.PageUrl,
            Category = AssetCategory.Clothes,
            Subtitle = package.Org,
            ThumbnailPath = package.Thumb,
            IsCommunity = true,
            CommunityIdent = package.Ident,
            CommunityUrl = package.PageUrl
        };
    }

    private sealed class CommunityPackageDto
    {
        public string Org { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ident { get; set; } = "";
        public string Title { get; set; } = "";
        public string Thumb { get; set; } = "";
        public string PageUrl { get; set; } = "";
    }
}
