using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DanmanAssetFinder.Models;

public sealed class AssetEntry : INotifyPropertyChanged
{
    private bool _isFavorite;
    private string? _thumbnailPath;
    private ImageSource? _thumbnailImage;
    private bool _isLoadingThumbnail;
    private bool _isSkeleton;

    public required string DisplayName { get; init; }
    public required string GamePath { get; init; }
    public required string FullPath { get; init; }
    public required AssetCategory Category { get; init; }
    public string? Subtitle { get; init; }

    /// <summary>Local disk path or remote http(s) URL for preview.</summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath == value)
                return;
            _thumbnailPath = value;
            OnPropertyChanged();
        }
    }

    public ImageSource? ThumbnailImage
    {
        get => _thumbnailImage;
        set
        {
            if (ReferenceEquals(_thumbnailImage, value))
                return;
            _thumbnailImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool IsLoadingThumbnail
    {
        get => _isLoadingThumbnail;
        set
        {
            if (_isLoadingThumbnail == value)
                return;
            _isLoadingThumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowSkeleton));
        }
    }

    public bool IsSkeleton
    {
        get => _isSkeleton;
        set
        {
            if (_isSkeleton == value)
                return;
            _isSkeleton = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowSkeleton));
        }
    }

    /// <summary>Bumped on unload so in-flight thumbnail loads are discarded.</summary>
    public int ThumbnailEpoch { get; set; }

    public bool HasThumbnail => ThumbnailImage is not null;
    public bool ShowSkeleton => IsSkeleton || (IsLoadingThumbnail && ThumbnailImage is null);

    public string? PreviewMeshPath { get; init; }
    public string? LinkedModelGamePath { get; init; }

    public bool IsCommunity { get; init; }
    public string? CommunityIdent { get; init; }
    public string? CommunityUrl { get; init; }

    public string FavoriteKey =>
        IsCommunity
            ? $"community:{CommunityIdent ?? GamePath}"
            : $"local:{GamePath}";

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;
            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
