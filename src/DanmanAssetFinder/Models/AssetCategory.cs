namespace DanmanAssetFinder.Models;

public enum AssetCategory
{
    Clothes,
    Props,
    Sounds,
    Entities
}

public static class AssetCategoryExtensions
{
    public static string DisplayName(this AssetCategory category) => category switch
    {
        AssetCategory.Clothes => "Clothes",
        AssetCategory.Props => "Props",
        AssetCategory.Sounds => "Sounds",
        AssetCategory.Entities => "Entities",
        _ => category.ToString()
    };

    public static IReadOnlyList<string> FileExtensions(this AssetCategory category) => category switch
    {
        AssetCategory.Clothes => [".clothing"],
        AssetCategory.Props => [".vmdl"],
        AssetCategory.Sounds => [".sound", ".sndef", ".vsnd"],
        AssetCategory.Entities => [".sent", ".prefab", ".entity"],
        _ => []
    };

    public static string DefaultFolderHint(this AssetCategory category) => category switch
    {
        AssetCategory.Clothes => @"...\addons\citizen\Assets\models\citizen_clothes",
        AssetCategory.Props => @"...\addons\citizen\Assets\models",
        AssetCategory.Sounds => @"...\Assets\sounds",
        AssetCategory.Entities => @"...\Assets\entities",
        _ => @"...\Assets"
    };
}
