namespace DanmanAssetFinder.Services;

/// <summary>
/// GitHub release source for update checks. Set Owner/Repo to your published repository.
/// </summary>
public static class AppReleaseInfo
{
    public const string Owner = "danman";
    public const string Repo = "danman_assetfinder";

    public static string ApiLatestReleaseUrl =>
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    public static string ReleasesPageUrl =>
        $"https://github.com/{Owner}/{Repo}/releases";

    public static string RepositoryPageUrl =>
        $"https://github.com/{Owner}/{Repo}";
}
