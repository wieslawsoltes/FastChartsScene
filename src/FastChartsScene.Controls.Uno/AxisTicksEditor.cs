using System.ComponentModel;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Controls.Uno;

/// <summary>Reusable draft-based axis tick editor. The complete candidate is validated before
/// assignment. Unrelated model updates preserve drafts; replacing Axis or Reset clears them.
/// This control does not own the axis. Use it on the UI thread.</summary>
public sealed class AxisTicksEditor : UserControl, IDisposable
{
    public static readonly DependencyProperty AxisProperty = DependencyProperty.Register(nameof(Axis), typeof(Axis),
        typeof(AxisTicksEditor), new PropertyMetadata(null, (d, _) => ((AxisTicksEditor)d).Rebind()));
    public Axis? Axis { get => (Axis?)GetValue(AxisProperty); set { ObjectDisposedException.ThrowIf(_disposed, this); SetValue(AxisProperty, value); } }
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<AxisTickMode>(), Header = "Tick mode" };
    private readonly TextBox _numeric = new() { Header = "Numeric format", PlaceholderText = "Automatic, F2, E3, G17 or R", MaxLength = 3 };
    private readonly TextBox _date = new() { Header = "Date format (UTC)", PlaceholderText = "Automatic or yyyy-MM-dd", MaxLength = 64 };
    private readonly TextBox _ticks = new() { Header = "Custom ticks: value|label", AcceptsReturn = true, MinHeight = 100, MaxHeight = 180,
        MaxLength = AxisTickText.MaximumTextLength, TextWrapping = TextWrapping.NoWrap, PlaceholderText = "0|Start\n5|Middle\n10|End" };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkRed), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private Axis? _subscribed;
    private AxisTickOptions? _seen;
    private bool _sync, _applying, _disposed;
    public bool HasDraft { get; private set; }
    public string ValidationError => _error.Text;
    public long TextConversionCount { get; private set; }
    public AxisTicksEditor()
    {
        var body = new StackPanel { Spacing = 5, Padding = new Thickness(5) };
        void Add(Control control, string name)
        {
            control.FontSize = 11; control.MinHeight = 25; control.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(control, "Ticks." + name); body.Children.Add(control);
        }
        Add(_mode, "Mode"); Add(_numeric, "Numeric format"); Add(_date, "Date format"); Add(_ticks, "Custom ticks");
        body.Children.Add(new TextBlock { Text = "No pipe: automatic label. Empty label after |: tick only. All values are data coordinates; at most 512 ticks.", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        var apply = new Button { Content = "Apply ticks" }; Add(apply, "Apply ticks"); apply.Click += (_, _) => ApplyDrafts();
        var reset = new Button { Content = "Reset tick drafts" }; Add(reset, "Reset tick drafts"); reset.Click += (_, _) => ResetDrafts();
        body.Children.Add(_error); Content = body;
        void Edited() { if (!_sync) { HasDraft = true; _error.Text = ""; } }
        _mode.SelectionChanged += (_, _) => Edited(); _numeric.TextChanged += (_, _) => Edited();
        _date.TextChanged += (_, _) => Edited(); _ticks.TextChanged += (_, _) => Edited();
        Loaded += (_, _) => { if (!_disposed) { Attach(); Refresh(); } };
        Unloaded += (_, _) => Detach();
    }
    private void Rebind()
    {
        Detach(); _seen = null; HasDraft = false;
        if (_disposed) return;
        if (IsLoaded) Attach(); Refresh(force: true);
    }
    private void Attach()
    {
        if (ReferenceEquals(_subscribed, Axis)) return;
        Detach(); _subscribed = Axis; if (_subscribed != null) _subscribed.PropertyChanged += Changed;
    }
    private void Detach() { if (_subscribed != null) _subscribed.PropertyChanged -= Changed; _subscribed = null; }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (!_applying && ReferenceEquals(sender, Axis)) Refresh(); }
    private void Refresh(bool force = false)
    {
        if (_disposed || !force && HasDraft) return;
        var options = Axis?.TickOptions;
        IsEnabled = options != null;
        if (!force && ReferenceEquals(options, _seen)) return;
        _sync = true;
        try
        {
            _mode.SelectedItem = options?.Mode ?? AxisTickMode.Automatic;
            _numeric.Text = options?.NumericFormat ?? ""; _date.Text = options?.DateTimeFormat ?? "";
            if (!ReferenceEquals(_seen?.CustomTicks, options?.CustomTicks) || force)
            { _ticks.Text = options == null ? "" : AxisTickText.Write(options.CustomTicks); TextConversionCount++; }
            _seen = options; _error.Text = ""; HasDraft = false;
        }
        finally { _sync = false; }
    }
    public bool ApplyDrafts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Axis is not { } axis) return false;
        if (!HasDraft) { Refresh(); return true; }
        try
        {
            var ticks = AxisTickText.Parse(_ticks.Text);
            var options = new AxisTickOptions(_mode.SelectedItem is AxisTickMode mode ? mode : AxisTickMode.Automatic, ticks, _numeric.Text, _date.Text);
            _applying = true; axis.TickOptions = options; HasDraft = false; Refresh(force: true); return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { _error.Text = error.Message; return false; }
        finally { _applying = false; }
    }
    public void ResetDrafts() { ObjectDisposedException.ThrowIf(_disposed, this); HasDraft = false; Refresh(force: true); }
    public new void Dispose() { if (_disposed) return; _disposed = true; Detach(); _seen = null; SetValue(AxisProperty, null); }
}
