using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;
public sealed record PropertySnapshot(int Rows, int VisibleRows, long FilterPasses, string Filter, string Error);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PropertySnapshot))]
internal partial class PropertyJsonContext : JsonSerializerContext { }
public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string InspectProperties()
    {
        var p = Page!; var grid = p.Properties;
        return JsonSerializer.Serialize(new PropertySnapshot(grid.RowCount, grid.VisibleRowCount, grid.FilterPasses, grid.Filter, p.PropertyError), PropertyJsonContext.Default.PropertySnapshot);
    }
    private static FrameworkElement PropertyElement(string category, string name) =>
        category == "$search" ? PropertySearchControl() : name == "$group" ? Page!.Properties.GroupHeader(category) : Page!.Properties.Editor(category, name);
#if __WASM__
    [JSExport]
#endif
    public static void BringPropertyIntoView(string category, string name) => PropertyElement(category, name).StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
#if __WASM__
    [JSExport]
#endif
    public static string PropertyBounds(string category, string name) => Bounds(PropertyElement(category, name));
#if __WASM__
    [JSExport]
#endif
    public static string PropertyOptionBounds(string category, string name, int index)
    {
        if (PropertyElement(category, name) is not ComboBox { IsDropDownOpen: true } combo || index < 0 || index >= combo.Items.Count || combo.ContainerFromIndex(index) is not FrameworkElement item) return "";
        return Bounds(item);
    }
#if __WASM__
    [JSExport]
#endif
    public static string PropertyText(string category, string name) => PropertyElement(category, name) switch
    {
        TextBox box => box.Text,
        NumberBox box => box.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ComboBox box => box.SelectedItem?.ToString() ?? "",
        CheckBox box => box.IsChecked == true ? "true" : "false",
        _ => ""
    };
    private static FrameworkElement PropertySearchControl()
    {
        DependencyObject? Find(DependencyObject n)
        {
            if (n is TextBox box && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == "Search properties") return box;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++) { var item = Find(VisualTreeHelper.GetChild(n, i)); if (item != null) return item; }
            return null;
        }
        return (FrameworkElement)Find(Page!)!;
    }
#if __WASM__
    [JSExport]
#endif
    public static async Task<string> CheckReusableControlLifetime() => await Page!.CheckCatalogLifetime();
}
public sealed partial class ExplorerPage
{
    internal async Task<string> CheckCatalogLifetime()
    {
        var model = new CatalogBrowserModel([new("a", "Alpha", "Group", "", []), new("b", "Beta", "Group", "", [])]);
        var grid = new PropertyGridControl(); var editor = new TextBox { Text = "draft" };
        using (grid.BeginUpdate()) { grid.Add("Group", "Value", editor); grid.Add("Group", "Other", new TextBox()); }
        if (grid.FilterPasses != 1) throw new InvalidOperationException("Batched property construction performed redundant passes.");
        grid.Filter = "value"; if (grid.VisibleRowCount != 1 || editor.Text != "draft") throw new InvalidOperationException("Property filtering lost editor state.");
        var passes = grid.FilterPasses; grid.Filter = " VALUE "; if (grid.FilterPasses != passes) throw new InvalidOperationException("Repeated filters were not cached.");
        grid.Clear(); if (VisualTreeHelper.GetParent(editor) != null) throw new InvalidOperationException("Clear retained editor parenting.");
        var gallery = new CatalogBrowserControl { Width = 300, Height = 300, Opacity = 0, IsHitTestVisible = false };
        gallery.SetModel(model, "a");
        try
        {
            try { gallery.SetModel(new CatalogBrowserModel([]), "missing"); throw new InvalidOperationException("Bad selection accepted."); }
            catch (ArgumentException) { if (!ReferenceEquals(gallery.Model, model)) throw new InvalidOperationException("Invalid SetModel was not atomic."); }
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gallery.Loaded += (_, _) => loaded.TrySetResult();
            _root.Children.Add(gallery);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gallery.Dispose(); _root.Children.Remove(gallery); // Exercise unload after explicit disposal.
            gallery.Dispose(); gallery.SuspendPreviews(); model.SetFavorite("a", true);
            try { gallery.SetSearch("a"); throw new InvalidOperationException("Disposed mutation accepted."); } catch (ObjectDisposedException) { }
            return "passed";
        }
        finally { _root.Children.Remove(gallery); gallery.Dispose(); }
    }
}
