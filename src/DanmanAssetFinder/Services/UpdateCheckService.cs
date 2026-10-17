using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DanmanAssetFinder.Services;

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Message);

public static partial class UpdateCheckService
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(AppReleaseInfo.ApiLatestReleaseUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    null,
                    AppReleaseInfo.ReleasesPageUrl,
                    "No GitHub releases published yet.");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString()
                : AppReleaseInfo.ReleasesPageUrl;
            var draft = root.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
            var prerelease = root.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();

            if (string.IsNullOrWhiteSpace(tag) || draft)
            {
                return new UpdateCheckResult(false, currentVersion, null, htmlUrl, "No usable release found.");
            }

            // Prefer stable over prerelease unless current build is also a prerelease letter-suffixed
            var latest = tag.Trim();
            var available = IsNewer(latest, currentVersion);

            // If latest is prerelease and current looks stable, still allow if tag is newer
            _ = prerelease;

            return new UpdateCheckResult(
                available,
                currentVersion,
                latest,
                htmlUrl ?? AppReleaseInfo.ReleasesPageUrl,
                available
                    ? $"Update available: {latest}"
                    : $"Up to date ({currentVersion})");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                false,
                currentVersion,
                null,
                AppReleaseInfo.ReleasesPageUrl,
                $"Update check failed: {ex.Message}");
        }
    }

    public static bool IsNewer(string latestRaw, string currentRaw)
    {
        var latest = Parse(latestRaw);
        var current = Parse(currentRaw);
        if (latest is null || current is null)
            return !string.Equals(NormalizeLabel(latestRaw), NormalizeLabel(currentRaw), StringComparison.OrdinalIgnoreCase)
                   && string.Compare(NormalizeLabel(latestRaw), NormalizeLabel(currentRaw), StringComparison.OrdinalIgnoreCase) > 0;

        var cmp = latest.Value.Version.CompareTo(current.Value.Version);
        if (cmp != 0)
            return cmp > 0;

        // Same numeric version: letter suffix a < b < ... < (no suffix = release)
        return CompareSuffix(latest.Value.Suffix, current.Value.Suffix) > 0;
    }

    private static (Version Version, string Suffix)? Parse(string raw)
    {
        var label = NormalizeLabel(raw);
        var match = VersionPattern().Match(label);
        if (!match.Success)
            return null;

        if (!Version.TryParse(match.Groups[1].Value, out var version))
            return null;

        return (version, match.Groups[2].Value.ToLowerInvariant());
    }

    private static string NormalizeLabel(string raw) =>
        raw.Trim().TrimStart('v', 'V');

    private static int CompareSuffix(string latest, string current)
    {
        // empty suffix = final release (highest)
        if (string.IsNullOrEmpty(latest) && string.IsNullOrEmpty(current))
            return 0;
        if (string.IsNullOrEmpty(latest))
            return 1;
        if (string.IsNullOrEmpty(current))
            return -1;
        return string.CompareOrdinal(latest, current);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DanmanAssetFinder");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+){0,3})([a-zA-Z]*)$")]
    private static partial Regex VersionPattern();
}
