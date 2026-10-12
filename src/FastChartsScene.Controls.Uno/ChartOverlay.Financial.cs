using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Skia;
using SkiaSharp;

namespace FastChartsScene.Controls.Uno;

internal sealed partial class ChartOverlay
{
    public FinancialDataHit? FinancialHit { get; set; }
    public string[] FinancialLines { get; set; } = [];
    public RectD? FinancialBounds { get; set; }

    private void DrawFinancialCursor(SKCanvas canvas, RenderFrame frame)
    {
        FinancialBounds = null;
        if (FinancialHit is not { } hit || Hit != null || Cursor is not { } cursor || !hit.PaneBounds.Contains(cursor)) return;
        var pane = hit.PaneBounds;
        canvas.Save(); canvas.ClipRect(SkiaFrameRenderer.Rect(pane));
        try
        {
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1; _paint.Color = new SKColor(210, 220, 230, 170);
            canvas.DrawLine((float)hit.Screen.X, (float)pane.Y, (float)hit.Screen.X, (float)pane.Bottom, _paint);
            canvas.DrawLine((float)pane.X, (float)hit.Screen.Y, (float)pane.Right, (float)hit.Screen.Y, _paint);
            _paint.StrokeWidth = 2; _paint.Color = SKColors.White;
            canvas.DrawCircle((float)hit.Screen.X, (float)hit.Screen.Y, 5, _paint);
            if (pane.Width < 100 || pane.Height < 48 || FinancialLines.Length == 0) return;
            var width = Math.Min(280, pane.Width - 4); var height = Math.Min(128, pane.Height - 4);
            var x = Math.Clamp(cursor.X + 15, pane.X + 2, pane.Right - width - 2);
            var y = Math.Clamp(cursor.Y - height - 10, pane.Y + 2, pane.Bottom - height - 2);
            var bounds = new RectD(x, y, width, height); FinancialBounds = bounds;
            _paint.Style = SKPaintStyle.Fill; _paint.Color = new SKColor(18, 21, 26, 246);
            canvas.DrawRoundRect((float)x, (float)y, (float)width, (float)height, 3, 3, _paint);
            _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1; _paint.Color = new SKColor(148, 156, 164, 220);
            canvas.DrawRoundRect((float)x, (float)y, (float)width, (float)height, 3, 3, _paint);
            canvas.ClipRect(SkiaFrameRenderer.Rect(new(x + 6, y + 2, width - 12, height - 4)));
            var selectedRow = hit.Field switch
            { FinancialValueField.Open => 2, FinancialValueField.High => 3, FinancialValueField.Low => 4, _ => 5 };
            for (var i = 0; i < FinancialLines.Length; i++)
            {
                var color = i == selectedRow ? "#FFEAA5FF" : i < 2 ? "#FFFFFFFF" : "#C8DCEFFF";
                _renderer.DrawLabel(canvas, new(FinancialLines[i], x + 8, y + 16 + i * 17, color, 11));
            }
        }
        finally { canvas.Restore(); }
    }
}
