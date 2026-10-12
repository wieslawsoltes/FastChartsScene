using FastChartsScene.Charts;
using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private readonly PropertyGridControl _propertyGrid = new();
    private readonly TextBlock _inspectorError = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkRed), Visibility = Visibility.Collapsed };
    private readonly List<Action> _propertyRefresh = [];
    private readonly List<Action> _axisDraftReset = [];
    private long _inspectorGeneration;
    private bool _inspectorUpdating;
    private Axis? _editedAxis;
    private NumberBox? _rangeMinimum, _rangeMaximum;
    private bool _rangeDirty;
    private ChartDocument? _inspectorDocument;
    private string? _selectedInspectorSeriesId;
    internal PropertyGridControl Properties => _propertyGrid;
    internal string PropertyError => _inspectorError.Text;
    private bool InspectorEdit(ChartDocument owner, Action write)
    {
        if (_inspectorUpdating || owner.IsDisposed || !ReferenceEquals(owner, Chart.Document)) return false;
        try { write(); _inspectorError.Text = ""; _inspectorError.Visibility = Visibility.Collapsed; return true; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { _inspectorError.Text = e.Message; _inspectorError.Visibility = Visibility.Visible; return false; }
    }
    private void RefreshInspector()
    {
        if (_inspectorUpdating || _inspectorDocument == null || _inspectorDocument.IsDisposed || !ReferenceEquals(_inspectorDocument, Chart.Document)) return; _inspectorUpdating = true;
        try
        {
            foreach (var refresh in _propertyRefresh) refresh();
            if (_editedAxis is { } axis && !_rangeDirty && _rangeMinimum != null && _rangeMaximum != null)
            {
                if (_rangeMinimum.FocusState == FocusState.Unfocused && _rangeMinimum.Value != axis.Range.Minimum) _rangeMinimum.Value = axis.Range.Minimum;
                if (_rangeMaximum.FocusState == FocusState.Unfocused && _rangeMaximum.Value != axis.Range.Maximum) _rangeMaximum.Value = axis.Range.Maximum;
            }
        }
        finally { _inspectorUpdating = false; }
    }
    private void BuildInspector()
    {
        _inspectorUpdating = true;
        try
        {
            var generation = ++_inspectorGeneration;
            var priorAxis = ReferenceEquals(_inspectorDocument, Chart.Document) ? _editedAxis : null;
            if (!ReferenceEquals(_inspectorDocument, Chart.Document)) _selectedInspectorSeriesId = null;
            _inspectorDocument = Chart.Document;
            _propertyRefresh.Clear(); _axisDraftReset.Clear(); _editedAxis = null; _rangeDirty = false;
            _rangeMinimum = _rangeMaximum = null; _inspectorError.Text = ""; _inspectorError.Visibility = Visibility.Collapsed;
            _propertyGrid.Clear(); _inspector.Children.Clear(); _inspector.Spacing = 4; _inspector.Padding = new Thickness(6);
            _inspector.Children.Add(new TextBlock { Text = "Properties Editor", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            _inspector.Children.Add(PropertyPresentationToolbar());
            _inspector.Children.Add(_propertySearch); _inspector.Children.Add(_inspectorError); _inspector.Children.Add(_propertyGrid);
            var d = Document;
            bool Edit(Action write) => generation == _inspectorGeneration && InspectorEdit(d, write);
            using var batch = _propertyGrid.BeginUpdate();
            Text("Chart", "Title", () => d.Title, v => d.Title = v);
            Text("Chart", "Subtitle", () => d.Subtitle, v => d.Subtitle = v);
            ReadOnly("Chart", "View", () => d.View.ToString());
            Choice("Chart", "ColorTheme", () => d.Theme == ChartTheme.Light ? "Light" : d.Theme == ChartTheme.HighContrast ? "High contrast" : "Dark",
                v => SetTheme(v == "Light" ? "light" : v == "High contrast" ? "contrast" : "dark"), ["Dark", "Light", "High contrast"]);
            Choice("Chart", "Backend", () => Chart.RenderingMode, v => Chart.RenderingMode = v, Enum.GetValues<ChartRenderingMode>());
            Boolean("Chart", "Grid", () => d.ShowGrid, v => d.ShowGrid = v);
            Boolean("Chart", "Legend", () => d.ShowLegend, v => d.ShowLegend = v);
            if (d.View == ChartView.XY)
                Choice("Chart", "AxisLayout", () => d.AxisLayout, v => d.AxisLayout = v,
                    d.Segments.Count > 0 ? Enum.GetValues<AxisLayout>() : [AxisLayout.Layered, AxisLayout.Stacked]);
            if (d.View is ChartView.ThreeD or ChartView.Pie3D)
            {
                Boolean("Camera", "Orthographic", () => d.Camera.Orthographic, v => d.Camera.Orthographic = v);
                Number("Camera", "Azimuth", () => d.Camera.Azimuth, v => d.Camera.Azimuth = v);
                Number("Camera", "Elevation", () => d.Camera.Elevation, v => d.Camera.Elevation = v);
                Number("Camera", "Distance", () => d.Camera.Distance, v => d.Camera.Distance = v);
            }
            _editedAxis = priorAxis != null && (d.XAxes.Contains(priorAxis) || d.YAxes.Contains(priorAxis) || ReferenceEquals(d.ZAxis, priorAxis)) ? priorAxis : d.XAxis;
            var axes = d.XAxes.Select(a => new AxisChoice("X", a)).Concat(d.YAxes.Select(a => new AxisChoice("Y", a))).Append(new AxisChoice("Z", d.ZAxis)).ToArray();
            var selector = new ComboBox { ItemsSource = axes, SelectedItem = axes.First(a => ReferenceEquals(a.Axis, _editedAxis)) };
            _propertyGrid.Add("Axis", "Selected axis", Compact(selector), DescribeProperty("Axis", "Selected axis"));
            selector.SelectionChanged += (_, _) =>
            {
                if (_inspectorUpdating || generation != _inspectorGeneration || selector.SelectedItem is not AxisChoice chosen) return;
                _editedAxis = chosen.Axis; _rangeDirty = false;
                _inspectorUpdating = true;
                try { foreach (var reset in _axisDraftReset) reset(); }
                finally { _inspectorUpdating = false; }
                _inspectorError.Text = ""; _inspectorError.Visibility = Visibility.Collapsed; RefreshInspector();
            };
            Text("Axis", "Title", () => _editedAxis!.Title, v => _editedAxis!.Title = v);
            Text("Axis", "Units", () => _editedAxis!.Units, v => _editedAxis!.Units = v);
            Choice("Axis", "Scale", () => _editedAxis!.Scale, v => _editedAxis!.Scale = v, Enum.GetValues<AxisScale>());
            Boolean("Axis", "Inverted", () => _editedAxis!.Inverted, v => _editedAxis!.Inverted = v);
            Choice("Axis", "Side", () => _editedAxis!.Side, v => _editedAxis!.Side = v, Enum.GetValues<AxisSide>());
            Number("Axis", "Weight", () => _editedAxis!.Weight, v => _editedAxis!.Weight = v);
            _rangeMinimum = new NumberBox { Value = _editedAxis.Range.Minimum }; _rangeMaximum = new NumberBox { Value = _editedAxis.Range.Maximum };
            _rangeMinimum.ValueChanged += (_, _) => { if (!_inspectorUpdating) _rangeDirty = true; };
            _rangeMaximum.ValueChanged += (_, _) => { if (!_inspectorUpdating) _rangeDirty = true; };
            _propertyGrid.Add("Axis", "Minimum", Compact(_rangeMinimum), DescribeProperty("Axis", "Minimum")); _propertyGrid.Add("Axis", "Maximum", Compact(_rangeMaximum), DescribeProperty("Axis", "Maximum"));
            var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            commands.Children.Add(Compact(Button("Apply range", () => Edit(() =>
            { _editedAxis!.Range = new AxisRange(_rangeMinimum.Value, _rangeMaximum.Value); _rangeDirty = false; RefreshInspector(); }))));
            commands.Children.Add(Compact(Button("Reset draft", () => { _rangeDirty = false; RefreshInspector(); _inspectorError.Text = ""; _inspectorError.Visibility = Visibility.Collapsed; })));
            _propertyGrid.Add("Axis", "Range commands", commands, "Apply both range bounds atomically or restore the current viewport.", true);
            // Only the selected object's editors are instantiated. A document may have
            // thousands of series without creating thousands of controls or frame watchers.
            var seriesOptions = d.Series.Select((s, index) => new SeriesChoice(index, s)).ToArray();
            var selected = seriesOptions.FirstOrDefault(s => s.Series.Id == _selectedInspectorSeriesId) ?? seriesOptions.FirstOrDefault();
            _selectedInspectorSeriesId = selected?.Series.Id;
            var seriesSelector = new ComboBox { ItemsSource = seriesOptions, SelectedItem = selected };
            _propertyGrid.Add("Series", "Selected series", Compact(seriesSelector), DescribeProperty("Series", "Selected series"));
            seriesSelector.SelectionChanged += (_, _) =>
            {
                if (_inspectorUpdating || generation != _inspectorGeneration || seriesSelector.SelectedItem is not SeriesChoice choice || choice.Series.Id == _selectedInspectorSeriesId) return;
                _selectedInspectorSeriesId = choice.Series.Id; BuildInspector();
            };
            if (selected != null)
            {
                var series = selected.Series; const string group = "Series";
                Text(group, "Title", () => series.Title, v => series.Title = v);
                Boolean(group, "Visible", () => series.Visible, v => series.Visible = v);
                Text(group, "Color", () => series.Color.ToHex(), v => series.Color = ColorRgba.Parse(v));
                Number(group, "StrokeWidth", () => series.StrokeWidth, v => series.StrokeWidth = v);
                Choice(group, "XAxis", () => series.XAxisId, v => { d.FindX(v); series.XAxisId = v; }, d.XAxes.Select(a => a.Id).ToArray());
                Choice(group, "YAxis", () => series.YAxisId, v => { d.FindY(v); series.YAxisId = v; }, d.YAxes.Select(a => a.Id).ToArray());
                var count = new TextBlock { Text = series.PointCount.ToString(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                var lastCount = series.PointCount; _propertyRefresh.Add(() => { var next = series.PointCount; if (next != lastCount) { count.Text = next.ToString(); lastCount = next; } });
                _propertyGrid.Add(group, "PointCount", count, DescribeProperty(group, "PointCount"));
                if (series is LineSeries line && d.View == ChartView.XY)
                {
                    Choice(group, "Marker", () => line.Marker, v => line.Marker = v, Enum.GetValues<MarkerShape>());
                    Number(group, "MarkerSize", () => line.MarkerSize, v => line.MarkerSize = v);
                    Choice(group, "Interpolation", () => line.Interpolation, v => line.Interpolation = v, Enum.GetValues<LineInterpolation>());
                    Boolean(group, "ShowLine", () => line.ShowLine, v => line.ShowLine = v);
                    Boolean(group, "FillToBaseline", () => line.FillToBaseline, v => line.FillToBaseline = v);
                    Number(group, "Baseline", () => line.Baseline, v => line.Baseline = v);
                }
                if (series is BarSeries bar)
                {
                    Boolean(group, "Horizontal", () => bar.Horizontal, v => bar.Horizontal = v);
                    Number(group, "BarWidth", () => bar.BarWidth, v => bar.BarWidth = v);
                    Number(group, "Baseline", () => bar.Baseline, v => bar.Baseline = v);
                    Text(group, "StackGroup", () => bar.StackGroup, v => bar.StackGroup = v);
                }
                if (series is FinancialSeries finance) Choice(group, "Style", () => finance.Style, v => finance.Style = v, Enum.GetValues<FinancialStyle>());
                if (series is PointLineSeries3D spatial)
                {
                    Boolean(group, "ShowLine", () => spatial.ShowLine, v => spatial.ShowLine = v);
                    Number(group, "PointSize", () => spatial.PointSize, v => spatial.PointSize = v);
                }
                if (series is PieSeries pie)
                {
                    Number(group, "InnerRadius", () => pie.InnerRadius, v => pie.InnerRadius = v);
                    Number(group, "Height", () => pie.Height, v => pie.Height = v);
                    Number(group, "Explosion", () => pie.Explosion, v => pie.Explosion = v);
                }
                if (series is HeatmapSeries heat) _propertyGrid.Add(group, "Color scale", new IntensityScaleEditor { Series = heat }, "Intensity palette and calibrated color range", true);
            }
            FilterProperties();

            void ReadOnly(string group, string name, Func<string> read) => _propertyGrid.Add(group, name, new TextBlock { Text = read(), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            void Text(string group, string name, Func<string> read, Action<string> write)
            {
                var box = new TextBox { Text = read() }; var dirty = false; var updating = false;
                _propertyGrid.Add(group, name, Compact(box), DescribeProperty(group, name));
                void Refresh()
                {
                    if (dirty || box.FocusState != FocusState.Unfocused) return;
                    var value = read(); if (box.Text == value) return;
                    updating = true; try { box.Text = value; } finally { updating = false; }
                }
                box.TextChanged += (_, _) => { if (!_inspectorUpdating && !updating) dirty = true; };
                box.LostFocus += (_, _) => { if (dirty && generation == _inspectorGeneration && Edit(() => write(box.Text))) { dirty = false; Refresh(); } };
                void Reset()
                {
                    dirty = false; updating = true;
                    try { box.Text = read(); } finally { updating = false; }
                }
                if (group == "Axis") _axisDraftReset.Add(Reset);
                box.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
                {
                    if (e.Key != Windows.System.VirtualKey.Escape || generation != _inspectorGeneration) return;
                    Reset(); _inspectorError.Text = ""; _inspectorError.Visibility = Visibility.Collapsed; e.Handled = true;
                }), true);
                _propertyRefresh.Add(Refresh);
            }
            void Number(string group, string name, Func<double> read, Action<double> write)
            {
                var box = new NumberBox { Value = read() }; _propertyGrid.Add(group, name, Compact(box), DescribeProperty(group, name));
                var updating = false;
                box.ValueChanged += (_, _) =>
                {
                    if (updating || _inspectorUpdating || generation != _inspectorGeneration) return;
                    if (!Edit(() => write(box.Value)))
                    { updating = true; try { box.Value = read(); } finally { updating = false; } }
                };
                _propertyRefresh.Add(() => { if (box.FocusState == FocusState.Unfocused && box.Value != read()) box.Value = read(); });
            }
            void Boolean(string group, string name, Func<bool> read, Action<bool> write)
            {
                var box = new CheckBox { IsChecked = read(), MinHeight = 24, Padding = new Thickness(0) }; _propertyGrid.Add(group, name, box, DescribeProperty(group, name));
                box.Checked += (_, _) => Edit(() => write(true)); box.Unchecked += (_, _) => Edit(() => write(false));
                _propertyRefresh.Add(() => { if (box.IsChecked != read()) box.IsChecked = read(); });
            }
            void Choice<T>(string group, string name, Func<T> read, Action<T> write, T[] choices)
            {
                var box = new ComboBox { ItemsSource = choices, SelectedItem = read() }; _propertyGrid.Add(group, name, Compact(box), DescribeProperty(group, name));
                box.SelectionChanged += (_, _) => { if (box.SelectedItem is T value) { Edit(() => write(value)); RefreshInspector(); } };
                _propertyRefresh.Add(() => { if (!Equals(box.SelectedItem, read())) box.SelectedItem = read(); });
            }
        }
        finally { _inspectorUpdating = false; }
    }
    private static T Compact<T>(T control) where T : Control
    { control.FontSize = 11; control.MinHeight = 24; control.Padding = new Thickness(4, 1, 4, 1); return control; }
    private sealed record SeriesChoice(int Index, Series Series)
    { public override string ToString() => $"{Index + 1}: {Series.Title}"; }
    private sealed record AxisChoice(string Dimension, Axis Axis)
    { public override string ToString() => $"{Dimension}: {Axis.Title} [{Axis.Id}]"; }
}
