namespace FloorPlan.Editor;

// Furniture catalogue. Most items are imported from the live arcada backend
// (see CatalogData.cs + wwwroot/furniture/*.svg). A few items (e.g. the
// compass) are custom-drawn and have no image.
public record CatalogItem(
    string Key,
    string Name,
    string Category,
    double Width,   // meters
    double Height,  // meters
    string Fill,
    string Symbol,        // short glyph drawn when there is no image
    string ImagePath = "" // relative svg path under wwwroot, "" = custom-drawn
);

public static partial class Catalog
{
    private static IReadOnlyList<string>? _categories;
    public static IReadOnlyList<string> Categories =>
        _categories ??= ImportedCategories.Append("Annotation").ToList();

    private static IReadOnlyList<CatalogItem>? _items;
    public static IReadOnlyList<CatalogItem> Items =>
        _items ??= Imported.Append(
            new("compass", "Compass", "Annotation", 0.8, 0.8, "#ffffff", "N")).ToList();

    public static CatalogItem? Get(string key) => Items.FirstOrDefault(i => i.Key == key);

    public static IEnumerable<CatalogItem> ByCategory(string category)
        => Items.Where(i => i.Category == category);

    public static CatalogItem Door => DoorImg;
    public static CatalogItem Window => WindowImg;
}
