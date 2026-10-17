# Danman S&box Asset Finder

Windows desktop tool (WPF / C#) to browse S&box asset pack folders for **clothes**, **props**, **sounds**, and **entities**. Copy game-relative paths for use in the editor or code, preview models when source meshes are available, and browse assets easier without having to open S&box editor. (It shouldn't be that hard in the first place)

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Windows (WPF)

## Run

```powershell
cd danman_assetfinder\src\DanmanAssetFinder
dotnet run
```

## Folder paths (examples)

Point each tab at the folder you want to scan. Paths are remembered per category.

| Tab | Example folder |
|-----|----------------|
| Clothes | `...\steamapps\common\sbox\addons\citizen\Assets\models\citizen_clothes` |
| Props | `...\Assets\models` (or a specific props subtree) |
| Sounds | `...\Assets\sounds` |
| Entities | `...\Assets\entities` |

The app walks upward to find an `Assets` root so copied paths look like `models/citizen_clothes/shirt/.../item.clothing`.

## Features

- **Find assets** pulls all the assets from the game folder
- **Community clothing** pulled from [sbox.game/ugc/clothing](https://sbox.game/ugc/clothing) (WebView2 scrape of the workshop page)
- **Favorites** star any local or community asset; persisted under `%LocalAppData%\DanmanAssetFinder`
- **Copy path** open community package page on sbox.game

## Notes

- Community tab needs the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already on Windows 10/11).
- First community refresh can take a few seconds while the workshop page loads.
- I don't have a linux build yet, but its planned soon after I fix all the current bugs and/or issues.
