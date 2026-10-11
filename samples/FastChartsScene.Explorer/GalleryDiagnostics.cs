using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed record GallerySnapshot(bool Open, bool Tiles, string Query, string? Selected, string[] Visible, string[] Favorites,
    long FilterPasses, int CachedPreviews, int GeneratedPreviews, int PreviewErrors, double Width, double Height);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GallerySnapshot))]
internal partial class GalleryJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string InspectGallery()
    {
        var page = Page ?? throw new InvalidOperationException("Explorer not initialized."); var gallery = page.Gallery; var model = gallery.Model!;
        return JsonSerializer.Serialize(new GallerySnapshot(page.GalleryIsOpen, gallery.IsTileView, gallery.SearchText, gallery.SelectedEntry?.Id,
            gallery.VisibleIds.ToArray(), model.Entries.Where(e => model.IsFavorite(e.Id)).Select(e => e.Id).ToArray(), model.FilterPasses,
            gallery.CachedPreviews, gallery.GeneratedPreviews, gallery.PreviewErrors, gallery.ActualWidth, gallery.ActualHeight), GalleryJsonContext.Default.GallerySnapshot);
    }
    private static FrameworkElement GalleryControl(string name)
    {
        DependencyObject? Find(DependencyObject node)
        {
            if (node is FrameworkElement && (AutomationProperties.GetName(node) == name || node is ContentControl c && c.Content is string text && text == name)) return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var result = Find(VisualTreeHelper.GetChild(node, i)); if (result != null) return result; }
            return null;
        }
        return (FrameworkElement?)Find(Page!.Gallery) ?? throw new ArgumentException("Gallery control not found: " + name);
    }
    private static string Bounds(FrameworkElement element)
    {
        var p = element.TransformToVisual(null).TransformPoint(new Point(0, 0));
        return JsonSerializer.Serialize(new RectD(p.X, p.Y, element.ActualWidth, element.ActualHeight), BrowserJsonContext.Default.RectD);
    }
#if __WASM__
    [JSExport]
#endif
    public static void BringGalleryControlIntoView(string name) => GalleryControl(name).StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, HorizontalAlignmentRatio = .5, VerticalAlignmentRatio = .5 });
#if __WASM__
    [JSExport]
#endif
    public static string GalleryControlBounds(string name) => Bounds(GalleryControl(name));
#if __WASM__
    [JSExport]
#endif
    public static void BringGalleryEntryIntoView(string id) => Page!.Gallery.BringEntryIntoView(id);
#if __WASM__
    [JSExport]
#endif
    public static string GalleryEntryBounds(string id) => Page!.Gallery.EntryContainer(id) is { } element ? Bounds(element) : "";
#if __WASM__
    [JSExport]
#endif
    public static string ExportFavorites() => Page!.ExportFavorites();
#if __WASM__
    [JSExport]
#endif
    public static void ImportFavorites(string json) => Page!.ImportFavorites(json);
}
