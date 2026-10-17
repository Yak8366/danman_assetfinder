using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DanmanAssetFinder.Models;
using DanmanAssetFinder.Services;
using Microsoft.Win32;

namespace DanmanAssetFinder.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public const int PageSize = 48;

    private readonly UserSettings _settings;
    private readonly HashSet<string> _favoriteKeys;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _communityCts;

    private AssetCategory _selectedCategory = AssetCategory.Clothes;
    private ClothingSourceFilter _clothingFilter = ClothingSourceFilter.All;
    private string _folderPath = string.Empty;
    private string _searchText = string.Empty;
    private AssetEntry? _selectedAsset;
    private string _statusText = "Select an asset pack folder to begin.";
    private bool _isScanning;
    private bool _isLoadingCommunity;
    private int _currentPage = 1;
    private int _totalFiltered;
    private bool _updateAvailable;
    private bool _isCheckingUpdates;
    private string? _latestVersion;
    private string? _updateUrl;
    private string _updateStatus = string.Empty;
    private List<AssetEntry> _localAssets = [];
    private List<AssetEntry> _communityAssets = [];
    private List<AssetEntry> _filtered = [];

    public MainViewModel()
    {
        _settings = UserSettingsStore.Load();
        _favoriteKeys = new HashSet<string>(_settings.FavoriteKeys, StringComparer.OrdinalIgnoreCase);
        FolderPath = UserSettingsStore.GetFolder(_settings, SelectedCategory) ?? string.Empty;

        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        RescanCommand = new RelayCommand(_ => _ = RescanLocalAsync(), _ => !IsScanning && Directory.Exists(FolderPath));
        CopyPathCommand = new RelayCommand(_ => CopyPath(), _ => SelectedAsset is not null && SelectedAsset is { IsSkeleton: false });
        CopyFullPathCommand = new RelayCommand(_ => CopyFullPath(), _ => SelectedAsset is { IsCommunity: false, IsSkeleton: false });
        ToggleFavoriteCommand = new RelayCommand(p => ToggleFavorite(p as AssetEntry), p => p is AssetEntry { IsSkeleton: false });
        OpenCommunityPageCommand = new RelayCommand(
            _ => OpenCommunityPage(),
            _ => SelectedAsset?.IsCommunity == true && !string.IsNullOrWhiteSpace(SelectedAsset.CommunityUrl));
        RefreshCommunityCommand = new RelayCommand(
            async _ => await RefreshCommunityAsync(force: true),
            _ => !IsLoadingCommunity && SelectedCategory == AssetCategory.Clothes);
        SetCategoryCommand = new RelayCommand(p =>
        {
            if (p is AssetCategory cat)
                SelectedCategory = cat;
            else if (p is string s && Enum.TryParse<AssetCategory>(s, out var parsed))
                SelectedCategory = parsed;
        });
        SetClothingFilterCommand = new RelayCommand(p =>
        {
            if (p is ClothingSourceFilter filter)
                ClothingFilter = filter;
            else if (p is string s && Enum.TryParse<ClothingSourceFilter>(s, out var parsed))
                ClothingFilter = parsed;
        });
        NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => CurrentPage < TotalPages);
        PrevPageCommand = new RelayCommand(_ => CurrentPage--, _ => CurrentPage > 1);
        OpenUpdatePageCommand = new RelayCommand(
            _ => OpenUpdatePage(),
            _ => UpdateAvailable && !string.IsNullOrWhiteSpace(_updateUrl));
        CheckForUpdatesCommand = new RelayCommand(
            async _ => await CheckForUpdatesAsync(),
            _ => !IsCheckingUpdates);

        _ = RescanLocalAsync();
        _ = CheckForUpdatesAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AssetEntry?>? SelectedAssetChanged;

    public ObservableCollection<AssetEntry> Assets { get; } = new();

    public AssetCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value)
                return;

            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClothesCategory));
            OnPropertyChanged(nameof(ShowFolderBar));
            FolderPath = UserSettingsStore.GetFolder(_settings, value) ?? string.Empty;
            _ = RescanLocalAsync();

            if (value == AssetCategory.Clothes
                && ClothingFilter is ClothingSourceFilter.All or ClothingSourceFilter.Community
                && _communityAssets.Count == 0)
            {
                _ = RefreshCommunityAsync(force: false);
            }
        }
    }

    public ClothingSourceFilter ClothingFilter
    {
        get => _clothingFilter;
        set
        {
            if (_clothingFilter == value)
                return;

            _clothingFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowFolderBar));
            ApplyFilter(resetPage: true);

            if (SelectedCategory == AssetCategory.Clothes
                && value is ClothingSourceFilter.Community or ClothingSourceFilter.All
                && _communityAssets.Count == 0
                && !IsLoadingCommunity)
            {
                _ = RefreshCommunityAsync(force: false);
            }
        }
    }

    public bool IsClothesCategory => SelectedCategory == AssetCategory.Clothes;

    public bool ShowFolderBar =>
        SelectedCategory != AssetCategory.Clothes
        || ClothingFilter is ClothingSourceFilter.All or ClothingSourceFilter.Local;

    public string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0]
        ?? "V0.0.1a";

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (_folderPath == value)
                return;

            _folderPath = value;
            OnPropertyChanged();
            UserSettingsStore.SetFolder(_settings, SelectedCategory, value);
            UserSettingsStore.Save(_settings);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;

            _searchText = value;
            OnPropertyChanged();
            ApplyFilter(resetPage: true);
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            var clamped = Math.Clamp(value, 1, Math.Max(1, TotalPages));
            if (_currentPage == clamped)
                return;

            _currentPage = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageLabel));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            RenderPage();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_totalFiltered / (double)PageSize));

    public string PageLabel => _totalFiltered == 0
        ? "0 items"
        : $"PAGE {CurrentPage} / {TotalPages}  ·  {_totalFiltered} ITEMS";

    public bool CanGoPrev => CurrentPage > 1;
    public bool CanGoNext => CurrentPage < TotalPages;

    public AssetEntry? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (ReferenceEquals(_selectedAsset, value))
                return;

            _selectedAsset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedIsFavorite));
            SelectedAssetChanged?.Invoke(value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool SelectedIsFavorite => SelectedAsset?.IsFavorite == true;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsLoadingCommunity
    {
        get => _isLoadingCommunity;
        private set
        {
            _isLoadingCommunity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy => IsScanning || IsLoadingCommunity;

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set
        {
            _updateAvailable = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            _isCheckingUpdates = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckUpdatesLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string CheckUpdatesLabel => IsCheckingUpdates ? "CHECKING" : "CHECK FOR UPDATES";

    public string? LatestVersion
    {
        get => _latestVersion;
        private set
        {
            _latestVersion = value;
            OnPropertyChanged();
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            _updateStatus = value;
            OnPropertyChanged();
        }
    }

    public ICommand BrowseFolderCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand CopyFullPathCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand OpenCommunityPageCommand { get; }
    public ICommand RefreshCommunityCommand { get; }
    public ICommand SetCategoryCommand { get; }
    public ICommand SetClothingFilterCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand OpenUpdatePageCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = $"Select {SelectedCategory.DisplayName()} folder",
            InitialDirectory = Directory.Exists(FolderPath) ? FolderPath : null
        };

        if (dialog.ShowDialog() != true)
            return;

        FolderPath = dialog.FolderName;
        _ = RescanLocalAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates)
            return;

        IsCheckingUpdates = true;
        UpdateStatus = "Checking for updates…";
        try
        {
            var result = await UpdateCheckService.CheckAsync(AppVersion);
            UpdateAvailable = result.UpdateAvailable;
            LatestVersion = result.LatestVersion;
            _updateUrl = result.ReleaseUrl;
            UpdateStatus = result.Message ?? string.Empty;
        }
        finally
        {
            IsCheckingUpdates = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OpenUpdatePage()
    {
        var url = _updateUrl ?? AppReleaseInfo.ReleasesPageUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task RescanLocalAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        if (!Directory.Exists(FolderPath))
        {
            _localAssets = [];
            ApplyFilter(resetPage: true);
            StatusText = string.IsNullOrWhiteSpace(FolderPath)
                ? "Choose a folder containing S&box assets."
                : "Folder not found. Browse to a valid asset pack path.";
            return;
        }

        IsScanning = true;
        StatusText = "Scanning local assets...";
        ShowSkeletons(12);

        var folder = FolderPath;
        var category = SelectedCategory;

        try
        {
            var scanned = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return AssetScanner.Scan(folder, category)
                    .Select(ApplyFavoriteFlag)
                    .ToList();
            }, token);

            token.ThrowIfCancellationRequested();
            _localAssets = scanned;
            ApplyFilter(resetPage: true);
            StatusText = $"{_localAssets.Count} local {category.DisplayName().ToLowerInvariant()}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _localAssets = [];
            ApplyFilter(resetPage: true);
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsScanning = false;
        }
    }

    private async Task RefreshCommunityAsync(bool force)
    {
        if (SelectedCategory != AssetCategory.Clothes)
            return;

        if (!force && _communityAssets.Count > 0)
            return;

        if (IsLoadingCommunity && !force)
            return;

        _communityCts?.Cancel();
        _communityCts = new CancellationTokenSource();
        var token = _communityCts.Token;

        IsLoadingCommunity = true;
        StatusText = "Loading community clothing from sbox.game...";

        if (ClothingFilter is ClothingSourceFilter.Community or ClothingSourceFilter.All)
            ShowSkeletons(Math.Min(PageSize, 18));

        try
        {
            await Task.Yield();
            var community = await CommunityClothingService.FetchAsync(token);
            token.ThrowIfCancellationRequested();

            _communityAssets = community.Select(ApplyFavoriteFlag).ToList();
            ApplyFilter(resetPage: true);
            StatusText = _communityAssets.Count > 0
                ? $"{_communityAssets.Count} community clothing items"
                : "No community items found. Check your connection and try Refresh.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = $"Community load failed: {ex.Message}";
            ApplyFilter(resetPage: true);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoadingCommunity = false;
        }
    }

    private void ShowSkeletons(int count)
    {
        Assets.Clear();
        for (var i = 0; i < count; i++)
        {
            Assets.Add(new AssetEntry
            {
                DisplayName = string.Empty,
                GamePath = $"skeleton-{i}",
                FullPath = string.Empty,
                Category = SelectedCategory,
                IsSkeleton = true,
                IsLoadingThumbnail = true
            });
        }
    }

    private AssetEntry ApplyFavoriteFlag(AssetEntry entry)
    {
        entry.IsFavorite = _favoriteKeys.Contains(entry.FavoriteKey);
        return entry;
    }

    private void ApplyFilter(bool resetPage)
    {
        var query = _searchText.Trim();

        IEnumerable<AssetEntry> source = SelectedCategory == AssetCategory.Clothes
            ? ClothingFilter switch
            {
                ClothingSourceFilter.Local => _localAssets,
                ClothingSourceFilter.Community => _communityAssets,
                ClothingSourceFilter.Favorite => _localAssets.Concat(_communityAssets).Where(a => a.IsFavorite),
                _ => _localAssets.Concat(_communityAssets)
            }
            : _localAssets;

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(a =>
                a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || a.GamePath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (a.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (a.CommunityIdent?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        _filtered = source
            .OrderByDescending(a => a.IsFavorite)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _totalFiltered = _filtered.Count;
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageLabel));

        if (resetPage)
            _currentPage = 1;
        else
            _currentPage = Math.Clamp(_currentPage, 1, TotalPages);

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        RenderPage();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RenderPage()
    {
        // Unload bitmaps from previous page to free RAM (keep selected preview alive)
        foreach (var old in Assets)
        {
            if (!old.IsSkeleton && !ReferenceEquals(old, SelectedAsset))
                ThumbnailLoader.Unload(old);
        }

        Assets.Clear();

        var pageItems = _filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        foreach (var asset in pageItems)
            Assets.Add(asset);

        // Lazy-load only this page's thumbnails
        ThumbnailLoader.QueueMany(pageItems);

        // Encourage reclaim of unloaded BitmapImages
        if (pageItems.Count > 0)
            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
    }

    private void ToggleFavorite(AssetEntry? asset)
    {
        if (asset is null || asset.IsSkeleton)
            return;

        asset.IsFavorite = !asset.IsFavorite;
        UserSettingsStore.SetFavorite(_settings, asset.FavoriteKey, asset.IsFavorite);

        if (asset.IsFavorite)
            _favoriteKeys.Add(asset.FavoriteKey);
        else
            _favoriteKeys.Remove(asset.FavoriteKey);

        UserSettingsStore.Save(_settings);
        OnPropertyChanged(nameof(SelectedIsFavorite));

        if (ClothingFilter == ClothingSourceFilter.Favorite)
            ApplyFilter(resetPage: false);

        StatusText = asset.IsFavorite
            ? $"Favorited {asset.DisplayName}"
            : $"Removed {asset.DisplayName} from favorites";
    }

    private void CopyPath()
    {
        if (SelectedAsset is null || SelectedAsset.IsSkeleton)
            return;

        Clipboard.SetText(SelectedAsset.GamePath);
        StatusText = $"Copied: {SelectedAsset.GamePath}";
    }

    private void CopyFullPath()
    {
        if (SelectedAsset is null || SelectedAsset.IsCommunity || SelectedAsset.IsSkeleton)
            return;

        Clipboard.SetText(SelectedAsset.FullPath);
        StatusText = "Copied full disk path.";
    }

    private void OpenCommunityPage()
    {
        if (SelectedAsset?.CommunityUrl is not { Length: > 0 } url)
            return;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
