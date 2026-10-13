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
    { Rows = new(_rows); InitializeComponent(); HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch; }
    public void SetValues(IReadOnlyList<SeriesCursorValue> values, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (IsHeld) return;
        if (!active) { Status.Text = _rows.Count == 0 ? "Cursor values · move over a chart" : $"Last sample · {_rows.Count} series"; return; }
        for (var i = 0; i < values.Count; i++)
        {
            if (i >= _rows.Count) { _rows.Add(new(values[i])); CreatedRows++; UpdatedRows++; }
            else if (_rows[i].Update(values[i])) UpdatedRows++;
        }
        while (_rows.Count > values.Count) _rows.RemoveAt(_rows.Count - 1);
        Status.Text = values.Count == 0 ? "No eligible line or financial values in this pane" : $"Nearest X · {values.Count} series · original values";
        UpdateDetails();
    }
    public void Clear()
    { Hold.IsChecked = false; _rows.Clear(); Details.Text = ""; Status.Text = "Cursor values · move over a chart"; }
    public IReadOnlyList<SeriesCursorValue> Snapshot() => _rows.Select(r => r.Value).ToArray();
    public string ExportCsv()
    {
        var text = new StringBuilder("SeriesId,SeriesTitle,Index,X,Value,Field,XAxis,YAxis,Open,High,Low,Close,Volume\r\n");
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        foreach (var row in _rows)
        {
            var v = row.Value; var c = v.Candle;
            text.Append(Q(v.SeriesId)).Append(',').Append(Q(v.Title)).Append(',').Append(v.Index.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CursorResultRow.Exact(v.Data.X)).Append(',').Append(CursorResultRow.Exact(v.Data.Y)).Append(',').Append(Q(row.FieldText)).Append(',')
                .Append(Q(v.XAxisId)).Append(',').Append(Q(v.YAxisId)).Append(',')
                .Append(c is { } a ? CursorResultRow.Exact(a.Open) : "").Append(',').Append(c is { } b ? CursorResultRow.Exact(b.High) : "").Append(',')
                .Append(c is { } d ? CursorResultRow.Exact(d.Low) : "").Append(',').Append(c is { } e ? CursorResultRow.Exact(e.Close) : "").Append(',')
                .Append(c is { } f ? CursorResultRow.Exact(f.Volume) : "").Append("\r\n");
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
    public CursorResultRow(SeriesCursorValue value) { Set(value); }
    internal static string Exact(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    internal bool Update(SeriesCursorValue value)
    {
        if (Value == value) return false;
        Set(value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); return true;
    }
    private void Set(SeriesCursorValue value)
    {
        var color = value.Color;
        if (Value.Color != color) Swatch = new(Windows.UI.Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)));
        Value = value; IndexText = value.Index.ToString(CultureInfo.InvariantCulture);
        XText = value.Data.X.ToString("G9", CultureInfo.InvariantCulture); YText = value.Data.Y.ToString("G9", CultureInfo.InvariantCulture);
        FieldText = value.Field?.ToString() ?? "Y";
        DetailText = $"{value.Title} [{value.XAxisId}/{value.YAxisId}]  Index {IndexText}  X {Exact(value.Data.X)}  {FieldText} {Exact(value.Data.Y)}";
        if (value.Candle is { } c) DetailText += $"  Open {Exact(c.Open)}  High {Exact(c.High)}  Low {Exact(c.Low)}  Close {Exact(c.Close)}  Volume {Exact(c.Volume)}";
    }
}
