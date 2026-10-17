using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using DanmanAssetFinder.Models;
using DanmanAssetFinder.Services;
using DanmanAssetFinder.ViewModels;
using HelixToolkit.Wpf;

namespace DanmanAssetFinder;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private CancellationTokenSource? _previewCts;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.SelectedAssetChanged += OnSelectedAssetChanged;
    }

    private void AssetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not AssetEntry asset)
            return;

        if (asset.IsSkeleton)
            return;

        if (e.OriginalSource is DependencyObject source
            && FindAncestor<System.Windows.Controls.Button>(source) is not null
            && !ReferenceEquals(FindAncestor<System.Windows.Controls.Button>(source), element))
        {
            return;
        }

        _viewModel.SelectedAsset = asset;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void OnSelectedAssetChanged(AssetEntry? asset)
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        PreviewTitle.Text = asset?.IsSkeleton == true ? "—" : asset?.DisplayName ?? "—";
        PreviewSubtitle.Text = asset?.IsCommunity == true
            ? $"Community · {asset.Subtitle}"
            : asset?.Subtitle ?? string.Empty;
        PreviewGamePath.Text = asset?.IsSkeleton == true ? string.Empty : asset?.GamePath ?? string.Empty;
        PreviewModelPath.Text = asset?.LinkedModelGamePath ?? string.Empty;

        PreviewViewport.Children.Clear();
        PreviewViewport.Children.Add(new DefaultLights());
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewViewport.Visibility = Visibility.Collapsed;

        if (asset is null || asset.IsSkeleton)
        {
            PreviewPlaceholder.Text = "SELECT AN ASSET";
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        if (asset.ThumbnailImage is not null)
        {
            PreviewImage.Source = asset.ThumbnailImage;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewPlaceholder.Text = "LOADING PREVIEW";
        PreviewPlaceholder.Visibility = Visibility.Visible;

        try
        {
            var path = asset.ThumbnailPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var local = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? await CommunityThumbnailCache.GetLocalPathAsync(path, token)
                    : path;

                token.ThrowIfCancellationRequested();

                var image = CommunityThumbnailCache.LoadBitmapFromFile(local);
                if (image is not null)
                {
                    asset.ThumbnailImage = image;
                    PreviewImage.Source = image;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            token.ThrowIfCancellationRequested();

            var model = BuildPreviewModel(asset);
            if (model is not null)
            {
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewViewport.Visibility = Visibility.Visible;
                PreviewViewport.Children.Add(new ModelVisual3D { Content = model });
                PreviewViewport.ZoomExtents(500);
                return;
            }

            PreviewPlaceholder.Text = asset.Category == AssetCategory.Sounds
                ? "SOUND — NO PREVIEW"
                : "NO PREVIEW";
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // selection changed
        }
    }

    private static Model3D? BuildPreviewModel(AssetEntry asset)
    {
        if (asset.IsCommunity)
            return null;

        if (!string.IsNullOrWhiteSpace(asset.PreviewMeshPath))
        {
            var meshModel = MeshPreviewBuilder.BuildFromFile(asset.PreviewMeshPath);
            if (meshModel is not null)
                return meshModel;
        }

        if (!asset.FullPath.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            return null;

        var meshGame = VmdlMeshExtractor.ExtractPrimaryMeshGamePath(asset.FullPath);
        if (string.IsNullOrWhiteSpace(meshGame))
            return null;

        var assetsRoot = AssetPathHelper.FindAssetsRoot(asset.FullPath);
        var meshPath = AssetPathHelper.ResolveMeshBesideAsset(asset.FullPath, meshGame, assetsRoot)
            ?? (assetsRoot is not null ? AssetPathHelper.ResolveGamePathToAbsolute(assetsRoot, meshGame) : null);

        return string.IsNullOrWhiteSpace(meshPath) ? null : MeshPreviewBuilder.BuildFromFile(meshPath);
    }
}
