using FastChartsScene.Data;

namespace FastChartsScene.Charts;

public enum FinancialValueField { Open, High, Low, Close }

public sealed partial class FinancialSeries
{
    private FinancialValueField _cursorField = FinancialValueField.Close;
    private bool _pointsOrdered = true;
    private readonly IDataSource?[] _priceSources = new IDataSource?[4];

    /// <summary>Original price used by the financial data cursor. Defaults to Close;
    /// changing this property does not alter candle geometry or stored OHLC values.</summary>
    public FinancialValueField CursorField
    {
        get => _cursorField;
        set { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _cursorField, value); }
    }

    /// <summary>Immutable, allocation-free-per-sample XY view over one owned OHLC snapshot.
    /// Repeated calls for a field reuse the view. A previously obtained view remains valid
    /// after SetData replaces the series. No numeric arrays are copied or synthesized.
    /// Suitable for independent indicators, line overlays and indexed source queries.
    /// Obtain views on the same UI thread as the series; the returned data is immutable.</summary>
    public IDataSource GetPriceData(FinancialValueField field)
    {
        if (!Enum.IsDefined(field)) throw new ArgumentOutOfRangeException(nameof(field));
        return _priceSources[(int)field] ??= new PriceSource(_points, _pointsOrdered, field);
    }

    private void ResetPriceSources()
    {
        Array.Clear(_priceSources);
        _pointsOrdered = true;
        for (var i = 1; i < _points.Length; i++)
            if (_points[i].X < _points[i - 1].X) { _pointsOrdered = false; break; }
    }

    private sealed class PriceSource(OhlcPoint[] points, bool ordered, FinancialValueField field) : IDataSource
    {
        public int Count => points.Length;
        public bool IsOrdered => ordered;
        public double X(int index) => points[index].X;
        public double Y(int index)
        {
            var point = points[index];
            return field switch
            {
                FinancialValueField.Open => point.Open,
                FinancialValueField.High => point.High,
                FinancialValueField.Low => point.Low,
                _ => point.Close
            };
        }
    }
}
