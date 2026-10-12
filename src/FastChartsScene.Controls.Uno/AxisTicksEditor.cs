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
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<AxisTickMode>() };
    private readonly TextBox _numeric = new() { PlaceholderText = "Automatic, F2, 0.00 or 0.0E+00", MaxLength = AxisTickOptions.MaximumNumericFormatLength };
    private readonly TextBox _date = new() { PlaceholderText = "Automatic or yyyy-MM-dd", MaxLength = 64 };
    private readonly TextBox _ticks = new() { Header = "Custom ticks: value|label", AcceptsReturn = true, MinHeight = 100, MaxHeight = 180,
        MaxLength = AxisTickText.MaximumTextLength, TextWrapping = TextWrapping.NoWrap, PlaceholderText = "0|Start\n5|Middle\n10|End" };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkRed), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private Axis? _subscribed;
    private AxisTickOptions? _seen;
    private bool _sync, _applying, _disposed;
    private readonly record struct Draft(AxisTickMode Mode, string Numeric, string Date, string Ticks);
    private Draft _baseline = new(AxisTickMode.Automatic, "", "", "");
    private Draft _observed = new(AxisTickMode.Automatic, "", "", "");
    // Native text notifications can arrive after a programmatic refresh has returned.
    // Compare actual contents with the displayed baseline, not the timing of TextChanged.
    public bool HasDraft => !_disposed && !_sync && !Same(ReadDraft(), _baseline);
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
        // Reuse the same native property/value presentation as the parent inspector.
        // Editors remain the original instances: layout does not rebuild or commit drafts.
        var formats = new PropertyGridControl { NameColumnWidth = 88 };
        using (formats.BeginUpdate())
        {
            formats.Add("Ticks", "Mode", _mode, "Automatic positions or exact custom data-coordinate ticks.");
            formats.Add("Ticks", "Numeric format", _numeric, "Invariant numeric format; empty retains adaptive precision.");
            formats.Add("Ticks", "Date format", _date, "Invariant date/time format in UTC; empty uses automatic labels.");
        }
        foreach (var control in new Control[] { _mode, _numeric, _date })
        {
            control.FontSize = 11; control.MinHeight = 24;
            control.Padding = new Thickness(4, 1, 4, 1);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        body.Children.Add(formats);
        Add(_ticks, "Custom ticks");
        body.Children.Add(new TextBlock { Text = "No pipe: automatic label. Empty label after |: tick only. All values are data coordinates; at most 512 ticks.", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        var apply = new Button { Content = "Apply ticks" }; Add(apply, "Apply ticks"); apply.Click += (_, _) => ApplyDrafts();
        var reset = new Button { Content = "Reset tick drafts" }; Add(reset, "Reset tick drafts"); reset.Click += (_, _) => ResetDrafts();
        body.Children.Add(_error); Content = body;
        void Edited()
        {
            if (_sync || _disposed) return;
            var current = ReadDraft();
            if (Same(current, _observed)) return;
            _observed = current; _error.Text = "";
        }
        _mode.SelectionChanged += (_, _) => Edited(); _numeric.TextChanged += (_, _) => Edited();
        _date.TextChanged += (_, _) => Edited(); _ticks.TextChanged += (_, _) => Edited();
        Loaded += (_, _) => { if (!_disposed) { Attach(); Refresh(); } };
        Unloaded += (_, _) => Detach();
    }
    private Draft ReadDraft() => new(_mode.SelectedItem is AxisTickMode mode ? mode : AxisTickMode.Automatic, _numeric.Text, _date.Text, _ticks.Text);
    private static bool Same(Draft a, Draft b) => a.Mode == b.Mode && a.Numeric == b.Numeric && a.Date == b.Date && SameLines(a.Ticks, b.Ticks);
    private static bool SameLines(string a, string b)
    {
        if (ReferenceEquals(a, b)) return true;
        // CR, LF and CRLF are native representations of the same editor line ending.
        // Compare without allocating a normalized copy on every model notification.
        var i = 0; var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var x = a[i++]; var y = b[j++];
            if (x == '\r') { if (i < a.Length && a[i] == '\n') i++; x = '\n'; }
            if (y == '\r') { if (j < b.Length && b[j] == '\n') j++; y = '\n'; }
            if (x != y) return false;
        }
        return i == a.Length && j == b.Length;
    }
    private void Rebind()
    {
        Detach(); _seen = null;
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
            _seen = options; _error.Text = ""; _baseline = _observed = ReadDraft();
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
            _applying = true; axis.TickOptions = options; Refresh(force: true); return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { _error.Text = error.Message; return false; }
        finally { _applying = false; }
    }
    public void ResetDrafts() { ObjectDisposedException.ThrowIf(_disposed, this); Refresh(force: true); }
    public new void Dispose() { if (_disposed) return; _disposed = true; Detach(); _seen = null; SetValue(AxisProperty, null); }
}
