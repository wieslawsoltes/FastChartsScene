using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Skia;
using SkiaSharp;

namespace FastChartsScene.Controls.Uno;

internal sealed partial class ChartOverlay
{
    public ScalarDataHit? ScalarHit { get; set; }
    private void DrawScalarCursor(SKCanvas canvas, RenderFrame frame)
    {
        if (ScalarHit is not { } hit || Hit != null || Cursor is not { } cursor || !frame.Layout.Plot.Contains(cursor) || frame.Layout.Plot.Width < 20 || frame.Layout.Plot.Height < 20) return;
        canvas.Save(); canvas.ClipRect(SkiaFrameRenderer.Rect(frame.Layout.Plot));
        _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1.5f; _paint.Color = SKColors.White;
        canvas.DrawRect(SkiaFrameRenderer.Rect(hit.CellBounds), _paint);
        var pane = frame.Layout.Plot; var width = Math.Min(266, pane.Width); var height = Math.Min(58, pane.Height);
        var x = Math.Clamp(cursor.X + 14, pane.X, Math.Max(pane.X, pane.Right - width));
        var y = Math.Clamp(cursor.Y - height - 8, pane.Y, Math.Max(pane.Y, pane.Bottom - height));
        _paint.Style = SKPaintStyle.Fill; _paint.Color = new SKColor(15, 18, 22, 240);
        canvas.DrawRoundRect((float)x, (float)y, (float)width, (float)height, 4, 4, _paint);
        var value = hit.Value is { } number ? number.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) : "No data";
        string[] lines = [$"{hit.Title} · cell [{hit.Column},{hit.Row}]", FormattableString.Invariant($"X {hit.Data.X:G7}   Y {hit.Data.Y:G7}"), $"{hit.ValueTitle}: {value} {hit.ValueUnits}".TrimEnd()];
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i]; var size = Math.Min(11, (width - 14) / Math.Max(1, text.Length * .6));
            _renderer.DrawLabel(canvas, new(text, x + 7, y + (i + 1) * height / 3 - 5, i == 2 ? "#FFEAA5FF" : "#FFFFFFFF", size));
        }
        canvas.Restore();
    }
}
