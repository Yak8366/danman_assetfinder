using System.Text.Json;

namespace DanmanAssetFinder.Services;

public static class GameResourceParser
{
    public static ParsedResource Parse(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!IsJsonLikeResource(ext))
            return ParsedResource.Empty;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = doc.RootElement;

            return new ParsedResource(
                Title: ReadString(root, "Title", "DisplayName", "Name"),
                Model: ReadString(root, "Model", "HumanAltModel", "Prefab"),
                Icon: ReadString(root, "Icon", "MenuIcon"),
                Description: ReadString(root, "Description", "Category"));
        }
        catch
        {
            return ParsedResource.Empty;
        }
    }

    private static bool IsJsonLikeResource(string ext) =>
        ext.Equals(".clothing", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".sound", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".sndef", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".sent", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".entity", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }

        return null;
    }
}

public readonly record struct ParsedResource(
    string? Title,
    string? Model,
    string? Icon,
    string? Description)
{
    public static ParsedResource Empty => new(null, null, null, null);
}
