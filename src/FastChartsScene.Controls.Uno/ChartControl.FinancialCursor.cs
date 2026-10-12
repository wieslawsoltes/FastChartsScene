using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using Microsoft.UI.Xaml.Automation;

namespace FastChartsScene.Controls.Uno;

public sealed partial class ChartControl
{
    /// <summary>Nearest tracked original OHLC candle. Competes with the XY sample cursor
    /// by projected distance; exact ties prefer the later series. Cleared on leaving,
    /// replacement, unload, disabling tracking, or absence of an eligible sample.</summary>
    public FinancialDataHit? FinancialHit => _overlay.FinancialHit;
    public event EventHandler? FinancialHitChanged;
    private long _financialTextRevision = -1;
    private string _financialText = "";
    /// <summary>Accessible, axis-formatted OHLC/volume readout. Numeric values are available
    /// without display rounding in FinancialHit.Sample. Empty when no financial hit is active.</summary>
    public string FinancialCursorText => _financialText;
    public long FinancialTextFormatCount { get; private set; }
    /// <summary>Last drawn readout bounds in chart-local device-independent coordinates.</summary>
    public RectD? FinancialCursorBounds => _overlay.FinancialBounds;

    private void SetFinancialHit(FinancialDataHit? hit)
    {
        var previous = _overlay.FinancialHit;
        var changed = !Equals(previous, hit);
        _overlay.FinancialHit = hit;
        if (hit == null)
        {
            _overlay.FinancialBounds = null; _overlay.FinancialLines = []; _financialText = ""; _financialTextRevision = -1;
            if (previous != null) AutomationProperties.SetItemStatus(this, "");
        }
        else if (Document is { } d && (previous == null || previous.SeriesId != hit.SeriesId || previous.Index != hit.Index ||
            previous.Sample != hit.Sample || previous.Field != hit.Field || _financialTextRevision != d.Revision))
        {
            var x = d.FindX(hit.XAxisId); var y = d.FindY(hit.YAxisId);
            string Value(double value) => y.Format(value);
            _overlay.FinancialLines = [hit.Title + " · candle " + hit.Index,
                "X  " + x.Format(hit.Sample.X) + (x.Units.Length > 0 ? " " + x.Units : ""),
                "Open   " + Value(hit.Sample.Open), "High   " + Value(hit.Sample.High),
                "Low    " + Value(hit.Sample.Low), "Close  " + Value(hit.Sample.Close),
                "Volume " + hit.Sample.Volume.ToString("R", System.Globalization.CultureInfo.InvariantCulture)];
            _financialText = string.Join("\n", _overlay.FinancialLines);
            _financialTextRevision = d.Revision; FinancialTextFormatCount++;
            AutomationProperties.SetItemStatus(this, _financialText);
        }
        if (changed) FinancialHitChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ClearCursorHits()
    { SetDataHit(null); SetScalarHit(null); SetFinancialHit(null); }

    private void UpdateCursorHits(RenderFrame frame, PointD location)
    {
        if (_input == null || Document is not { } document) { ClearCursorHits(); return; }
        var data = _input.HitTestData(frame, location);
        var financial = _input.HitTestFinancial(frame, location);
        if (data != null && financial != null)
        {
            bool FinancialIsLater()
            {
                for (var i = document.Series.Count - 1; i >= 0; i--)
                {
                    if (document.Series[i].Id == financial.SeriesId) return true;
                    if (document.Series[i].Id == data.SeriesId) return false;
                }
                return false;
            }
            if (financial.Distance < data.Distance || financial.Distance == data.Distance && FinancialIsLater()) data = null;
            else financial = null;
        }
        // Query all results before raising public events. Event handlers may replace the
        // document; never subsequently publish results from the old owner in that case.
        var scalar = _input.HitTestScalar(frame, location);
        SetDataHit(data);
        if (!ReferenceEquals(document, Document)) return;
        SetScalarHit(scalar);
        if (!ReferenceEquals(document, Document)) return;
        SetFinancialHit(financial);
    }
}
