using System.ComponentModel;
using System.Globalization;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FastChartsScene.Controls.Uno;

/// <summary>Reusable, bindable scalar scale editor. Invalid edits leave the model untouched
/// and are reported inline. The caller owns Series; subscriptions detach when the editor unloads.</summary>
public sealed class IntensityScaleEditor : UserControl, IDisposable
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(nameof(Series), typeof(HeatmapSeries), typeof(IntensityScaleEditor),
        new PropertyMetadata(null, (sender, args) => ((IntensityScaleEditor)sender).ReplaceSeries((HeatmapSeries?)args.OldValue, (HeatmapSeries?)args.NewValue)));
    public HeatmapSeries? Series { get => (HeatmapSeries?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public string? LastError { get; private set; }
    private bool _sync, _attached, _disposed, _applying;
    private ColorMap? _listedPalette, _writtenPalette;
    private bool _stopsDirty, _rangeDirty, _missingDirty;
    /// <summary>Number of actual palette-to-text conversions, not refresh calls.</summary>
    public long PaletteTextBuildCount { get; private set; }
    public bool HasDrafts => _stopsDirty || _rangeDirty || _missingDirty;
    private readonly StackPanel _panel = new() { Spacing = 9 };
    private readonly ComboBox _palette = new() { Header = "Palette", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _interpolation = new() { Header = "Palette interpolation", ItemsSource = Enum.GetValues<ColorInterpolation>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _mode = new() { Header = "Color scale", ItemsSource = Enum.GetValues<ColorScaleMode>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _outside = new() { Header = "Out-of-range values", ItemsSource = Enum.GetValues<ColorOutOfRange>(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _reversed = new() { Content = "Reverse palette" };
    private readonly CheckBox _automatic = new() { Content = "Automatic color range" };
    private readonly CheckBox _bar = new() { Content = "Show colorbar" };
    private readonly NumberBox _minimum = new() { Header = "Color minimum" };
    private readonly NumberBox _maximum = new() { Header = "Color maximum" };
    private readonly TextBox _title = new() { Header = "Value title", MaxLength = 200 };
    private readonly TextBox _units = new() { Header = "Value units", MaxLength = 80 };
    private readonly TextBox _missing = new() { Header = "Missing color (RRGGBBAA)", MaxLength = 9 };
    private readonly TextBox _stops = new() { Header = "Custom stops: offset, color", AcceptsReturn = true, Height = 110, MaxLength = 32768 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Button _applyRange = new() { Content = "Apply color range", HorizontalAlignment = HorizontalAlignment.Stretch };
    public IntensityScaleEditor()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        foreach (var control in new Control[] { _palette, _interpolation, _mode, _outside, _reversed, _automatic, _bar, _minimum, _maximum, _title, _units, _missing, _stops })
        {
            var name = control switch { ComboBox combo => combo.Header?.ToString(), NumberBox number => number.Header?.ToString(), TextBox text => text.Header?.ToString(), CheckBox check => check.Content?.ToString(), _ => "Intensity setting" };
            AutomationProperties.SetName(control, name ?? "Intensity setting");
        }
        foreach (var control in new UIElement[] { _palette, _interpolation, _mode, _reversed, _automatic, _minimum, _maximum, _applyRange, _outside, _bar, _title, _units, _missing }) _panel.Children.Add(control);
        AddButton("Apply missing color", () => Change(scale => scale.WithMissingColor(ColorRgba.Parse(_missing.Text)), () => _missingDirty = false));
        _panel.Children.Add(_stops);
        AddButton("Apply custom palette", () => Change(scale => scale.WithPalette(ParseStops(_stops.Text, scale.Palette.Interpolation)), () => { _stopsDirty = false; _writtenPalette = null; }));
        AddButton("Reset editor drafts", ResetDrafts);
        _panel.Children.Add(_error); Content = _panel;
        _palette.SelectionChanged += (_, _) => { if (_palette.SelectedItem is ColorMap palette) Change(scale => scale.WithPalette(palette.WithInterpolation(scale.Palette.Interpolation))); };
        _interpolation.SelectionChanged += (_, _) => { if (_interpolation.SelectedItem is ColorInterpolation mode) Change(scale => scale.WithPalette(scale.Palette.WithInterpolation(mode))); };
        _mode.SelectionChanged += (_, _) => { if (_mode.SelectedItem is ColorScaleMode mode) Change(scale => scale.WithMode(mode)); };
        _outside.SelectionChanged += (_, _) => { if (_outside.SelectedItem is ColorOutOfRange outside) Change(scale => scale.WithOutOfRange(outside)); };
        _reversed.Checked += (_, _) => Change(scale => scale.WithReversed(true)); _reversed.Unchecked += (_, _) => Change(scale => scale.WithReversed(false));
        _automatic.Checked += (_, _) => Change(scale => scale.WithRange(null));
        _automatic.Unchecked += (_, _) => Apply(() => Series!.ColorScale = Series.ColorScale.WithRange(ManualRange(Series)));
        _bar.Checked += (_, _) => Apply(() => Series!.ShowColorBar = true); _bar.Unchecked += (_, _) => Apply(() => Series!.ShowColorBar = false);
        _title.TextChanged += (_, _) => Apply(() => Series!.ValueTitle = _title.Text); _units.TextChanged += (_, _) => Apply(() => Series!.ValueUnits = _units.Text);
        _applyRange.Click += (_, _) => Change(scale => scale.WithRange(new(_minimum.Value, _maximum.Value)), () => _rangeDirty = false);
        _stops.TextChanged += (_, _) => { if (!_sync) _stopsDirty = true; };
        _missing.TextChanged += (_, _) => { if (!_sync) _missingDirty = true; };
        _minimum.ValueChanged += (_, _) => { if (!_sync) _rangeDirty = true; };
        _maximum.ValueChanged += (_, _) => { if (!_sync) _rangeDirty = true; };
        Loaded += (_, _) => { if (_disposed || _attached) return; _attached = true; if (Series is { } series) series.PropertyChanged += ModelChanged; Refresh(); };
        Unloaded += (_, _) => Detach(); Refresh();
    }
    private void AddButton(string title, Action action)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, title); button.Click += (_, _) => action(); _panel.Children.Add(button);
    }
    private static AxisRange ManualRange(HeatmapSeries series)
    {
        var scale = series.ResolveColorScale();
        if (!scale.HasValues) return series.ColorScale.Mode == ColorScaleMode.Logarithmic ? new(1, 10) : new(0, 1);
        if (!scale.IsConstant) return new(scale.Minimum, scale.Maximum);
        if (series.ColorScale.Mode != ColorScaleMode.Logarithmic) return AxisRange.FromData(scale.Minimum, scale.Maximum);
        return new(Math.Max(double.Epsilon, scale.Minimum / 2), Math.Min(double.MaxValue, scale.Maximum * 2));
    }
    private static ColorMap ParseStops(string text, ColorInterpolation interpolation)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > ColorMap.MaximumStops) throw new FormatException("A palette supports at most 256 stops.");
        var stops = lines.Select(line =>
        {
            var pair = line.Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
                throw new FormatException("Each stop must be offset, #RRGGBB or offset, #RRGGBBAA.");
            return new ColorStop(offset, ColorRgba.Parse(pair[1]));
        });
        return new("Custom", stops, interpolation);
    }
    private void Change(Func<ColorScale, ColorScale> transform, Action? committed = null) => Apply(() => Series!.ColorScale = transform(Series.ColorScale), committed);
    private void Apply(Action action, Action? committed = null)
    {
        if (_sync || _disposed || Series == null) return;
        _applying = true;
        try { action(); committed?.Invoke(); LastError = null; }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { LastError = error.Message; }
        finally { _applying = false; }
        // Retain invalid draft text/ranges so users can correct them, rather than losing their edit.
        if (LastError == null) Refresh(); else ShowError();
    }
    private void ReplaceSeries(HeatmapSeries? old, HeatmapSeries? current)
    {
        if (_attached && old != null) old.PropertyChanged -= ModelChanged;
        if (_attached && current != null) current.PropertyChanged += ModelChanged;
        _stopsDirty = _rangeDirty = _missingDirty = false; _writtenPalette = null;
        LastError = null; Refresh();
    }
    /// <summary>Discard only this editor's uncommitted palette/range/missing-color drafts.
    /// The model is not modified. Call on the UI thread.</summary>
    public void ResetDrafts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stopsDirty = _rangeDirty = _missingDirty = false; _writtenPalette = null;
        LastError = null; Refresh();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args) { if (!_applying) Refresh(); }
    private void ShowError()
    {
        _error.Text = LastError ?? ""; _error.Visibility = LastError == null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Refresh()
    {
        if (_sync || _disposed) return; _sync = true;
        try
        {
            IsEnabled = Series != null; ShowError();
            if (Series is not { } series) return;
            var scale = series.ColorScale; var resolved = series.ResolveColorScale();
            var preset = ColorMap.Presets.FirstOrDefault(p => p.Stops.SequenceEqual(scale.Palette.Stops));
            var selected = preset ?? scale.Palette;
            if (!ReferenceEquals(_listedPalette, selected))
            {
                _palette.ItemsSource = preset == null ? ColorMap.Presets.Append(scale.Palette).ToArray() : ColorMap.Presets;
                _listedPalette = selected;
            }
            _palette.SelectedItem = selected; _interpolation.SelectedItem = scale.Palette.Interpolation;
            _mode.SelectedItem = scale.Mode; _outside.SelectedItem = scale.OutOfRange; _reversed.IsChecked = scale.Reversed;
            _automatic.IsChecked = scale.Range == null; _bar.IsChecked = series.ShowColorBar;
            _minimum.IsEnabled = _maximum.IsEnabled = _applyRange.IsEnabled = scale.Range != null;
            if (!_rangeDirty)
            {
                _minimum.Value = resolved.HasValues ? resolved.Minimum : double.NaN;
                _maximum.Value = resolved.HasValues ? resolved.Maximum : double.NaN;
            }
            _title.Text = series.ValueTitle; _units.Text = series.ValueUnits;
            if (!_missingDirty) _missing.Text = scale.MissingColor.ToHex();
            // Unrelated series notifications and acquisition refreshes must neither discard
            // drafts nor repeatedly format an unchanged immutable palette.
            if (!_stopsDirty && !ReferenceEquals(_writtenPalette, scale.Palette))
            {
                _stops.Text = string.Join("\n", scale.Palette.Stops.Select(stop => FormattableString.Invariant($"{stop.Offset:R}, {stop.Color.ToHex()}")));
                _writtenPalette = scale.Palette; PaletteTextBuildCount++;
            }
        }
        finally { _sync = false; }
    }
    private void Detach() { if (_attached && Series is { } series) series.PropertyChanged -= ModelChanged; _attached = false; }
    public new void Dispose() { if (_disposed) return; Detach(); _disposed = true; Content = null; }
}
