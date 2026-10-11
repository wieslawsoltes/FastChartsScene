using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed record IntensitySeriesState(string Id, string Title, int Columns, int Rows, string Palette, string Interpolation,
    string Mode, bool Reversed, bool AutoRange, double? Minimum, double? Maximum, bool HasValues, bool ShowColorBar, RectD? ColorBar);
public sealed record ScalarCursorState(string SeriesId, int Column, int Row, double? Value, string Color, PointD Data, RectD CellBounds);
public sealed record IntensityState(IntensitySeriesState[] Series, ScalarCursorState? Cursor, string? EditorError);
[JsonSerializable(typeof(IntensityState))]
internal partial class BrowserJsonContext : JsonSerializerContext { }

public static partial class BrowserBridge
{
    private static DependencyObject? FindIntensityElement(DependencyObject node, Func<DependencyObject, bool> predicate)
    {
        if (predicate(node)) return node;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var result = FindIntensityElement(VisualTreeHelper.GetChild(node, i), predicate); if (result != null) return result; }
        return null;
    }
    private static FrameworkElement IntensityControl(string name)
    {
        var editor = FindIntensityElement(Page!, element => element is IntensityScaleEditor) ?? throw new InvalidOperationException("No intensity scale editor is displayed.");
        return (FrameworkElement?)FindIntensityElement(editor, element => element is FrameworkElement &&
            (AutomationProperties.GetName(element) == name || element is Button button && button.Content?.ToString() == name))
            ?? throw new ArgumentException("Intensity control not found: " + name);
    }
#if __WASM__
    [JSExport]
#endif
    public static string InspectIntensity()
    {
        var page = Page ?? throw new InvalidOperationException("Explorer not initialized.");
        var series = page.Document.Series.OfType<HeatmapSeries>().Select(heat =>
        {
            var scale = heat.ResolveColorScale(); RectD? bar = null;
            if (page.Chart.Frame?.Layout.ColorBars.TryGetValue(heat.Id, out var placement) == true) bar = placement.Strip;
            return new IntensitySeriesState(heat.Id, heat.Title, heat.Grid.Columns, heat.Grid.Rows, heat.ColorScale.Palette.Name,
                heat.ColorScale.Palette.Interpolation.ToString(), heat.ColorScale.Mode.ToString(), heat.ColorScale.Reversed, heat.ColorScale.Range == null,
                scale.HasValues ? scale.Minimum : null, scale.HasValues ? scale.Maximum : null, scale.HasValues, heat.ShowColorBar, bar);
        }).ToArray();
        var hit = page.Chart.ScalarHit;
        var cursor = hit == null ? null : new ScalarCursorState(hit.SeriesId, hit.Column, hit.Row, hit.Value, hit.Color.ToHex(), hit.Data, hit.CellBounds);
        var editor = FindIntensityElement(page, element => element is IntensityScaleEditor) as IntensityScaleEditor;
        return JsonSerializer.Serialize(new IntensityState(series, cursor, editor?.LastError), BrowserJsonContext.Default.IntensityState);
    }
#if __WASM__
    [JSExport]
#endif
    public static void BringIntensityControlIntoView(string name) => IntensityControl(name).StartBringIntoView(
        new BringIntoViewOptions { AnimationDesired = false, HorizontalAlignmentRatio = .5, VerticalAlignmentRatio = .5 });
#if __WASM__
    [JSExport]
#endif
    public static string IntensityControlBounds(string name)
    {
        var element = IntensityControl(name); var point = element.TransformToVisual(null).TransformPoint(new Point(0, 0));
        return JsonSerializer.Serialize(new RectD(point.X, point.Y, element.ActualWidth, element.ActualHeight), BrowserJsonContext.Default.RectD);
    }
}
