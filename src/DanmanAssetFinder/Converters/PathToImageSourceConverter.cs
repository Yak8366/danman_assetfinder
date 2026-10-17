using System.Globalization;
using System.IO;
using System.Windows.Data;
using DanmanAssetFinder.Services;

namespace DanmanAssetFinder.Converters;

/// <summary>Sync load for local files only. Remote URLs must go through ThumbnailLoader.</summary>
public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(path))
            return null;

        return CommunityThumbnailCache.LoadBitmapFromFile(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
