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
public sealed record PropertySnapshot(int Rows, int VisibleRows, long FilterPasses, string Filter, string Error, string SortMode, double NameColumnWidth, string? SelectedPath, string? Description, string[] Order, long LayoutPasses);
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
        return JsonSerializer.Serialize(new PropertySnapshot(grid.RowCount, grid.VisibleRowCount, grid.FilterPasses, grid.Filter, p.PropertyError, grid.SortMode.ToString(), grid.NameColumnWidth, grid.SelectedProperty?.Path, grid.SelectedProperty?.Description, grid.VisiblePropertyPaths.ToArray(), grid.LayoutPasses), PropertyJsonContext.Default.PropertySnapshot);
    }
    private static FrameworkElement PropertyElement(string category, string name) =>
        category == "$search" ? PropertyNamedControl("Search properties") : category == "$toolbar" ? PropertyNamedControl(name) : name == "$group" ? Page!.Properties.GroupHeader(category) : Page!.Properties.Editor(category, name);
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
    public static string PropertyViewportBounds(string category, string name)
    {
        for (var node = VisualTreeHelper.GetParent(PropertyElement(category, name)); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer viewport) return Bounds(viewport);
        throw new InvalidOperationException("Property editor has no scrolling viewport.");
    }
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
#if __WASM__
    [JSExport]
#endif
    public static string PropertyDividerBounds(string category, string name)
    {
        var editor = PropertyElement(category, name);
        if (editor.Parent is not Grid row || row.Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.Thumb>().FirstOrDefault() is not { } divider) return "";
        return Bounds(divider);
    }
#if __WASM__
    [JSExport]
#endif
    public static void SelectInspectedAxis(int index)
    {
        var selector = (ComboBox)Page!.Properties.Editor("Axis", "Selected axis");
        if (index < 0 || index >= selector.Items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        selector.SelectedIndex = index;
    }
    private static FrameworkElement PropertyNamedControl(string name)
    {
        DependencyObject? Find(DependencyObject n)
        {
            if (n is FrameworkElement element && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element) == name) return element;
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
        grid.Filter = "";
        grid.SortMode = PropertyGridSortMode.Alphabetical;
        if (!grid.VisiblePropertyPaths.SequenceEqual(new[] { "Group.Other", "Group.Value" }) || !ReferenceEquals(grid.Editor("Group", "Value"), editor) || editor.Text != "draft") throw new InvalidOperationException("Sorting replaced editors or produced the wrong order.");
        var layouts = grid.LayoutPasses; grid.SortMode = PropertyGridSortMode.Alphabetical;
        if (grid.LayoutPasses != layouts) throw new InvalidOperationException("Identical sorting rebuilt the layout.");
        grid.SelectProperty("Group", "Value"); if (grid.SelectedProperty?.Path != "Group.Value") throw new InvalidOperationException("Property selection failed.");
        foreach (var width in new[] { double.NaN, double.PositiveInfinity, 0, 221 })
        {
            try { grid.NameColumnWidth = width; throw new InvalidOperationException("Invalid column width accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }
        if (grid.NameColumnWidth != 94) throw new InvalidOperationException("Invalid width changed state.");
        try { grid.SortMode = (PropertyGridSortMode)999; throw new InvalidOperationException("Invalid sorting accepted."); } catch (ArgumentOutOfRangeException) { }
        grid.SortMode = PropertyGridSortMode.Categorized;
        if (!grid.VisiblePropertyPaths.SequenceEqual(new[] { "Group.Value", "Group.Other" })) throw new InvalidOperationException("Category order was not restored.");
        grid.Clear(); if (grid.SelectedProperty != null) throw new InvalidOperationException("Clear retained selection metadata.");
        var large = new PropertyGridControl();
        using (large.BeginUpdate()) for (var i = 0; i < 512; i++) large.Add("Group", "P" + i, new TextBox());
        if (large.FilterPasses != 1 || large.LayoutPasses != 1 || large.RowCount != 512) throw new InvalidOperationException("Large batched construction was not one pass.");
        var extra = new TextBox();
        try { large.Add("Group", "overflow", extra); throw new InvalidOperationException("Unbounded row accepted."); } catch (InvalidOperationException) when (large.RowCount == 512 && extra.Parent == null) { }
        large.Clear();
        if (VisualTreeHelper.GetParent(editor) != null) throw new InvalidOperationException("Clear retained editor parenting.");
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
