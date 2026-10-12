using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;
public sealed record AxisTickSnapshot(string AxisId, bool Expanded, bool HasDraft, string Error, long TextConversions,
    long TickGenerations, string[] Labels);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AxisTickSnapshot))]
internal partial class AxisTickJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
    private static FrameworkElement TickControl(string name)
    {
        if (name == "$expander") return Page!.Properties.Editor("Axis", "Ticks");
        if (name == "$axis") return Page!.Properties.Editor("Axis", "Selected axis");
        DependencyObject? Find(DependencyObject node)
        {
            if (node is FrameworkElement && AutomationProperties.GetName(node) == "Ticks." + name) return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = Find(VisualTreeHelper.GetChild(node, i)); if (child != null) return child; }
            return null;
        }
        return (FrameworkElement?)Find(Page!.TickEditor!) ?? throw new ArgumentException("Tick editor control not found: " + name);
    }
#if __WASM__
    [JSExport]
#endif
    public static string InspectAxisTicks()
    {
        var editor = Page!.TickEditor!;
        return JsonSerializer.Serialize(new AxisTickSnapshot(editor.Axis?.Id ?? "", ((Expander)TickControl("$expander")).IsExpanded,
            editor.HasDraft, editor.ValidationError, editor.TextConversionCount, editor.Axis?.TickGenerationCount ?? 0,
            Page.Chart.Frame?.Metadata.Labels.Select(label => label.Text).ToArray() ?? []), AxisTickJsonContext.Default.AxisTickSnapshot);
    }
#if __WASM__
    [JSExport]
#endif
    public static string TickControlBounds(string name) => Bounds(TickControl(name));
#if __WASM__
    [JSExport]
#endif
    public static string TickViewportBounds(string name)
    {
        for (var node = VisualTreeHelper.GetParent(TickControl(name)); node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll) return Bounds(scroll);
        throw new InvalidOperationException("Tick control has no scrolling viewport.");
    }
#if __WASM__
    [JSExport]
#endif
    public static string TickControlText(string name) => TickControl(name) switch
    { TextBox box => box.Text, ComboBox box => box.SelectedItem?.ToString() ?? "", _ => "" };
#if __WASM__
    [JSExport]
#endif
    public static string TickOptionBounds(string name, int index)
    {
        if (TickControl(name) is not ComboBox { IsDropDownOpen: true } box || box.ContainerFromIndex(index) is not FrameworkElement item) return "";
        return Bounds(item);
    }
#if __WASM__
    [JSExport]
#endif
    public static async Task<string> CheckTickEditorLifetime() => await Page!.ValidateTickEditorLifetime();
#if __WASM__
    [JSExport]
#endif
    public static void CheckTickHostUpdates()
    {
        // Explicit integration probe of public host model notifications, never editor values.
        var axis = Page!.TickEditor!.Axis!;
        for (var i = 0; i < 24; i++) axis.Title = "Host-updated axis " + i;
    }
}

public sealed partial class ExplorerPage
{
    internal async Task<string> ValidateTickEditorLifetime()
    {
        var axis = new FastChartsScene.Core.Axis("lifetime");
        var editor = new FastChartsScene.Controls.Uno.AxisTicksEditor { Axis = axis, Width = 230, Height = 500, Opacity = 0, IsHitTestVisible = false };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Loaded += (_, _) => loaded.TrySetResult(); editor.Unloaded += (_, _) => unloaded.TrySetResult();
        try
        {
            _root.Children.Add(editor); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var settings = axis.TickOptions; var conversions = editor.TextConversionCount;
            if (!editor.ApplyDrafts() || !ReferenceEquals(settings, axis.TickOptions) || editor.TextConversionCount != conversions)
                throw new InvalidOperationException("Unchanged Apply regenerated tick settings or text.");
            _root.Children.Remove(editor); await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            axis.TickOptions = new(numericFormat: "F2");
            if (editor.TextConversionCount != conversions) throw new InvalidOperationException("Unloaded tick editor retained its model subscription.");
            editor.Dispose(); editor.Dispose();
            try { editor.Axis = axis; throw new InvalidOperationException("Disposed axis assignment accepted."); } catch (ObjectDisposedException) { }
            try { editor.ApplyDrafts(); throw new InvalidOperationException("Disposed tick apply accepted."); } catch (ObjectDisposedException) { }
            try { editor.ResetDrafts(); throw new InvalidOperationException("Disposed reset accepted."); } catch (ObjectDisposedException) { }
            return "passed";
        }
        finally { _root.Children.Remove(editor); editor.Dispose(); }
    }
}
