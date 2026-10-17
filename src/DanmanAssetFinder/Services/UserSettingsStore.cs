using System.Text.Json;
using DanmanAssetFinder.Models;

namespace DanmanAssetFinder.Services;

public sealed class UserSettings
{
    public Dictionary<string, string> FolderPaths { get; set; } = new();
    public List<string> FavoriteKeys { get; set; } = [];
}

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DanmanAssetFinder",
            "settings.json");

    public static UserSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return new UserSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static string? GetFolder(UserSettings settings, AssetCategory category)
    {
        settings.FolderPaths.TryGetValue(category.ToString(), out var path);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static void SetFolder(UserSettings settings, AssetCategory category, string path)
    {
        settings.FolderPaths[category.ToString()] = path;
    }

    public static bool IsFavorite(UserSettings settings, string key) =>
        settings.FavoriteKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static void SetFavorite(UserSettings settings, string key, bool favorite)
    {
        var existing = settings.FavoriteKeys
            .FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (favorite)
        {
            if (existing is null)
                settings.FavoriteKeys.Add(key);
        }
        else if (existing is not null)
        {
            settings.FavoriteKeys.Remove(existing);
        }
    }
}
