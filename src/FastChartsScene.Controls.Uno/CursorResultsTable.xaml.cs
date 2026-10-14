using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using FastChartsScene.Interaction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Controls.Uno;

/// <summary>Reusable native, virtualized results table. Rows own value snapshots; no chart
/// or document is owned. SetValues updates existing row objects. Inactive input keeps the
/// last sample explicitly labelled; Clear resets snapshots and Hold on owner replacement.</summary>
public sealed partial class CursorResultsTable : UserControl
{
    private readonly ObservableCollection<CursorResultRow> _rows = [];
    public ReadOnlyObservableCollection<CursorResultRow> Rows { get; }
    public bool IsHeld => Hold.IsChecked == true;
    public long CreatedRows { get; private set; }
    public long UpdatedRows { get; private set; }
    public string StatusText => Status.Text;
    public event EventHandler? ExportRequested;
    public CursorResultsTable()
    { Rows = new(_rows); InitializeComponent(); _formatReady = true; UpdateFormatAvailability(); _trackingReady = true; SyncTrackingMode(); UpdateResultHeaders(); UpdateResizeAvailability(); HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; }
    public void SetValues(IReadOnlyList<SeriesCursorValue> values, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (IsHeld) return;
        if (!active) { Status.Text = _rows.Count == 0 ? "Cursor values · move over a chart" : $"Last sample · {_rows.Count} series"; return; }
        var calculated = false;
        for (var i = 0; i < values.Count; i++)
        {
            calculated |= values[i].IsInterpolated;
            if (i >= _rows.Count) { _rows.Add(new(values[i], NumberFormat)); CreatedRows++; UpdatedRows++; }
            else if (_rows[i].Update(values[i])) UpdatedRows++;
        }
        while (_rows.Count > values.Count) _rows.RemoveAt(_rows.Count - 1);
        UpdateResultHeaders(calculated);
        Status.Text = values.Count == 0 ? "No eligible line or financial values in this pane" : calculated ?
            $"{values.Count} series · Calc Y marks calculated values" : TrackingMode == SeriesCursorTrackingMode.Interpolated ?
            $"{values.Count} series · original values (no calculated rows)" : $"Nearest X · {values.Count} series · original values";
        UpdateDetails();
    }
    public void Clear()
    { Hold.IsChecked = false; _rows.Clear(); UpdateResultHeaders(); Details.Text = ""; Status.Text = "Cursor values · move over a chart"; }
    public IReadOnlyList<SeriesCursorValue> Snapshot() => _rows.Select(r => r.Value).ToArray();
    public string ExportCsv()
    {
        var calculated = _rows.Any(r => r.Value.IsInterpolated);
        var text = new StringBuilder("SeriesId,SeriesTitle,Index,X,Value,Field,XAxis,YAxis,Open,High,Low,Close,Volume");
        if (calculated) text.Append(",ResultKind,ResultX,ResultValue,EndIndex,EndX,EndValue,Interpolation");
        text.Append("\r\n");
        static string Q(string s)
        {
            var trimmed = s.AsSpan().TrimStart();
            if (!trimmed.IsEmpty && (trimmed[0] is '=' or '+' or '-' or '@' || s[0] is '\t' or '\r' or '\n')) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        foreach (var row in _rows)
        {
            var v = row.Value; var c = v.Candle;
            text.Append(Q(v.SeriesId)).Append(',').Append(Q(v.Title)).Append(',').Append(v.Index.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CursorResultRow.Exact(v.Data.X)).Append(',').Append(CursorResultRow.Exact(v.Data.Y)).Append(',').Append(Q(v.Field?.ToString() ?? "Y")).Append(',')
                .Append(Q(v.XAxisId)).Append(',').Append(Q(v.YAxisId)).Append(',')
                .Append(c is { } a ? CursorResultRow.Exact(a.Open) : "").Append(',').Append(c is { } b ? CursorResultRow.Exact(b.High) : "").Append(',')
                .Append(c is { } d ? CursorResultRow.Exact(d.Low) : "").Append(',').Append(c is { } e ? CursorResultRow.Exact(e.Close) : "").Append(',')
                .Append(c is { } f ? CursorResultRow.Exact(f.Volume) : "");
            if (calculated)
            {
                var result = v.ResultData; var interpolation = v.Interpolation;
                text.Append(',').Append(v.IsInterpolated ? "Interpolated" : "Original").Append(',')
                    .Append(CursorResultRow.Exact(result.X)).Append(',').Append(CursorResultRow.Exact(result.Y)).Append(',')
                    .Append(interpolation?.EndIndex.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                    .Append(interpolation is { } endX ? CursorResultRow.Exact(endX.EndData.X) : "").Append(',')
                    .Append(interpolation is { } endY ? CursorResultRow.Exact(endY.EndData.Y) : "").Append(',')
                    .Append(interpolation is { } method ? Q(method.Method.ToString()) : "");
            }
            text.Append("\r\n");
        }
        return text.ToString();
    }
    private void HoldChanged(object sender, RoutedEventArgs e)
    { if (Status != null) Status.Text = IsHeld ? $"Held snapshot · {_rows.Count} series" : "Live · move over the chart to refresh"; }
    private void ExportClicked(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, EventArgs.Empty);
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetails();
    private void UpdateDetails()
    {
        if (Details == null) return;
        var row = Results.SelectedItem as CursorResultRow ?? _rows.FirstOrDefault();
        Details.Text = row?.DetailText ?? "";
    }
}

public sealed class CursorResultRow : INotifyPropertyChanged
{
    public SeriesCursorValue Value { get; private set; }
    public string Title => Value.Title;
    public string IndexText { get; private set; } = "";
    public string XText { get; private set; } = "";
    public string YText { get; private set; } = "";
    public string FieldText { get; private set; } = "";
    public string DetailText { get; private set; } = "";
    public SolidColorBrush Swatch { get; private set; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly PropertyChangedEventArgs AllPropertiesChanged = new(null);
    private string _exactX = "", _exactY = "";
    private CursorValueFormat _format;
    public CursorResultRow(SeriesCursorValue value) : this(value, CursorValueFormat.Default) { }
    internal CursorResultRow(SeriesCursorValue value, CursorValueFormat format) { _format = format; Set(value, true); }
    internal bool SetFormat(CursorValueFormat format)
    {
        if (_format == format) return false;
        var previous = _format; _format = format;
        // A format-only update does not reformat exact details, change the source,
        // rebuild swatches or regenerate the other column's unchanged text.
        if (previous.XFormat != format.XFormat) XText = format.FormatX(Value.ResultData.X);
        if (previous.ValueFormat != format.ValueFormat) YText = format.FormatValue(Value.ResultData.Y);
        PropertyChanged?.Invoke(this, AllPropertiesChanged); return true;
    }
    internal static string Exact(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    internal bool Update(SeriesCursorValue value)
    {
        if (Value == value) return false;
        Set(value); PropertyChanged?.Invoke(this, AllPropertiesChanged); return true;
    }
    private void Set(SeriesCursorValue value, bool initial = false)
    {
        var previous = Value;
        var color = value.Color;
        if (initial || previous.Color != color)
            Swatch = new(Windows.UI.Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)));
        var result = value.ResultData; var previousResult = previous.ResultData;
        if (initial || previous.Index != value.Index || previous.Interpolation?.EndIndex != value.Interpolation?.EndIndex)
            IndexText = value.Index.ToString(CultureInfo.InvariantCulture) + (value.Interpolation is { } end ? "–" + end.EndIndex.ToString(CultureInfo.InvariantCulture) : "");
        if (initial || previousResult.X != result.X)
        { XText = _format.FormatX(result.X); _exactX = Exact(result.X); }
        if (initial || previousResult.Y != result.Y)
        { YText = _format.FormatValue(result.Y); _exactY = Exact(result.Y); }
        if (initial || previous.Field != value.Field || previous.IsInterpolated != value.IsInterpolated)
            FieldText = value.IsInterpolated ? "Calc Y" : value.Field?.ToString() ?? "Y";
        // Color-only and identity-only updates do not change the selected-row text.
        // Retain formatted numbers across unrelated title/color/axis notifications.
        if (initial || previous.Title != value.Title || previous.XAxisId != value.XAxisId || previous.YAxisId != value.YAxisId ||
            previous.Index != value.Index || previous.Data != value.Data || previous.Field != value.Field || previous.Candle != value.Candle ||
            previous.Interpolation != value.Interpolation)
        {
            DetailText = $"{value.Title} [{value.XAxisId}/{value.YAxisId}]  Index {IndexText}  X {_exactX}  {FieldText} {_exactY}";
            if (value.Interpolation is { } interpolation)
                DetailText = $"Interpolated {interpolation.Method} · {value.Title} [{value.XAxisId}/{value.YAxisId}]  X {_exactX}  Y {_exactY}  From {value.Index}: ({Exact(value.Data.X)}, {Exact(value.Data.Y)}) to {interpolation.EndIndex}: ({Exact(interpolation.EndData.X)}, {Exact(interpolation.EndData.Y)})";
            if (value.Candle is { } c) DetailText += $"  Open {Exact(c.Open)}  High {Exact(c.High)}  Low {Exact(c.Low)}  Close {Exact(c.Close)}  Volume {Exact(c.Volume)}";
        }
        Value = value;
    }
}
