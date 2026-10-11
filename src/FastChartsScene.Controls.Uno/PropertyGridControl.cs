using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Controls.Uno;

/// <summary>Compact, categorized presentation for caller-supplied editors. The host owns
/// model bindings and validation. Filtering never reconstructs editors or discards drafts.
/// Intended for bounded property sheets, not arbitrary-size data tables; maximum 512 rows.</summary>
public sealed class PropertyGridControl : UserControl
{
    public const int MaximumRows = 512;
    private readonly StackPanel _body = new() { Spacing = 0 };
    private readonly Dictionary<string, Section> _sections = new(StringComparer.Ordinal);
    private readonly List<Row> _rows = [];
    private string _filter = "";
    private int _updateDepth;
    private bool _dirty;
    public int RowCount => _rows.Count;
    public int VisibleRowCount => _rows.Count(r => r.Host.Visibility == Visibility.Visible);
    public long FilterPasses { get; private set; }
    public string Filter
    {
        get => _filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length > 512) throw new ArgumentException("Property search is limited to 512 characters.", nameof(value));
            var next = value.Trim();
            if (string.Equals(next, _filter, StringComparison.OrdinalIgnoreCase)) return;
            _filter = next; InvalidateFilter();
        }
    }
    public PropertyGridControl() { RequestedTheme = ElementTheme.Light; Content = _body; HorizontalContentAlignment = HorizontalAlignment.Stretch; }
    public void Add(string category, string name, FrameworkElement editor, string description = "", bool fullWidth = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(description);
        if (_rows.Count >= MaximumRows) throw new InvalidOperationException("Property sheet exceeds its 512-row limit.");
        if (_rows.Any(r => ReferenceEquals(r.Editor, editor) || r.Category == category && r.Name == name))
            throw new ArgumentException("Property names must be unique within a category, and editors cannot be shared between rows.");
        if (editor.Parent != null || VisualTreeHelper.GetParent(editor) != null) throw new ArgumentException("Editor already belongs to a visual parent.", nameof(editor));
        if (!_sections.TryGetValue(category, out var section))
        {
            var label = new TextBlock { Text = category, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var arrow = new SymbolIcon(Symbol.Forward) { Width = 12, Height = 12, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5), RenderTransform = new RotateTransform { Angle = 90 } };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            heading.Children.Add(arrow); heading.Children.Add(label);
            var button = new Button { Content = heading, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 27, Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(0), Background = Fill(225), BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(button, "Property group " + category);
            var rows = new StackPanel(); var host = new StackPanel(); host.Children.Add(button); host.Children.Add(rows);
            section = new Section(host, button, arrow, rows); _sections.Add(category, section); _body.Children.Add(host);
            var captured = section; button.Click += (_, _) => { captured.Expanded = !captured.Expanded; InvalidateFilter(); };
        }
        Grid.SetColumn(editor, fullWidth ? 0 : 1);
        var row = new Grid { MinHeight = 28, Background = Fill(_rows.Count % 2 == 0 ? (byte)250 : (byte)245),
            BorderBrush = Fill(220), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(5, 1, 2, 1) };
        if (!fullWidth)
        {
            row.ColumnDefinitions.Add(new() { Width = new GridLength(94) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 4, 0) };
            ToolTipService.SetToolTip(label, description.Length == 0 ? name : name + "\n" + description); row.Children.Add(label);
            Grid.SetColumn(editor, 1);
        }
        AutomationProperties.SetName(editor, category + "." + name);
        if (description.Length != 0) AutomationProperties.SetHelpText(editor, description);
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        row.Children.Add(editor); section.Rows.Children.Add(row);
        var item = new Row(category, name, description, editor, row);
        _rows.Add(item); section.Items.Add(item); InvalidateFilter();
    }
    public FrameworkElement Editor(string category, string name) =>
        _rows.FirstOrDefault(r => r.Category == category && r.Name == name)?.Editor ?? throw new ArgumentException("Unknown property: " + category + "." + name);
    public Button GroupHeader(string category) => _sections.TryGetValue(category, out var s) ? s.Button : throw new ArgumentException("Unknown property group: " + category);
    public void Clear()
    {
        // Release visual parenting as well as metadata so caller-owned editors can be reused.
        foreach (var row in _rows) row.Host.Children.Clear();
        _body.Children.Clear(); _sections.Clear(); _rows.Clear();
    }
    /// <summary>Batch construction/filter changes into one linear pass.</summary>
    public IDisposable BeginUpdate() { _updateDepth++; return new UpdateScope(this); }
    private void InvalidateFilter() { if (_updateDepth != 0) _dirty = true; else ApplyFilter(); }
    private sealed class UpdateScope(PropertyGridControl grid) : IDisposable
    {
        private PropertyGridControl? _owner = grid;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null); if (owner == null) return;
            if (--owner._updateDepth == 0 && owner._dirty) { owner._dirty = false; owner.ApplyFilter(); }
        }
    }
    private void ApplyFilter()
    {
        FilterPasses++;
        foreach (var pair in _sections)
        {
            var section = pair.Value; var visible = 0;
            foreach (var row in section.Items)
            {
                var match = _filter.Length == 0 || row.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                    row.Category.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.Description.Contains(_filter, StringComparison.OrdinalIgnoreCase);
                var show = match && (section.Expanded || _filter.Length != 0);
                row.Host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (match) visible++;
            }
            section.Host.Visibility = visible == 0 ? Visibility.Collapsed : Visibility.Visible;
            section.Arrow.RenderTransform = new RotateTransform { Angle = section.Expanded || _filter.Length != 0 ? 90 : 0 };
            AutomationProperties.SetHelpText(section.Button, section.Expanded ? "Expanded" : "Collapsed");
        }
    }
    private static SolidColorBrush Fill(byte v) => new(Windows.UI.Color.FromArgb(255, v, v, v));
    private sealed record Row(string Category, string Name, string Description, FrameworkElement Editor, Grid Host);
    private sealed class Section(StackPanel host, Button button, SymbolIcon arrow, StackPanel rows)
    {
        public StackPanel Host { get; } = host;
        public Button Button { get; } = button;
        public SymbolIcon Arrow { get; } = arrow;
        public StackPanel Rows { get; } = rows;
        public bool Expanded { get; set; } = true;
        public List<Row> Items { get; } = [];
    }
}
