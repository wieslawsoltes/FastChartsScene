using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Controls.Uno;

public enum PropertyGridSortMode { Categorized, Alphabetical }
public sealed record PropertyGridSelection(string Category, string Name, string Description)
{
    public string Path => Category + "." + Name;
}

/// <summary>UI-thread-affine, bounded property sheet with caller-owned editors. Filtering,
/// sorting and resizing retain each editor instance. Model bindings/validation belong to the host.</summary>
public sealed class PropertyGridControl : UserControl
{
    public const int MaximumRows = 512;
    private readonly StackPanel _body = new() { Spacing = 0 };
    private readonly Dictionary<string, Section> _sections = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Category, string Name), Row> _byName = new();
    private readonly HashSet<FrameworkElement> _editors = new(ReferenceEqualityComparer.Instance);
    private readonly List<Row> _rows = [];
    private Row[]? _alphabetical;
    private Row? _selected;
    private string _filter = "";
    private int _updateDepth;
    private bool _dirty, _layoutDirty;
    private double _nameColumnWidth = 94;
    private PropertyGridSortMode _sortMode;
    public int RowCount => _rows.Count;
    public int VisibleRowCount => _rows.Count(r => r.Host.Visibility == Visibility.Visible);
    public long FilterPasses { get; private set; }
    public long LayoutPasses { get; private set; }
    public PropertyGridSelection? SelectedProperty => _selected?.Info;
    public event EventHandler? SelectedPropertyChanged;
    public PropertyGridSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_sortMode == value) return;
            _sortMode = value; Invalidate(layout: true);
        }
    }
    /// <summary>Requested name-column width in DIPs, from 64 to 220. Narrow hosts reserve
    /// at least 80 DIPs for values where possible. Drag any row divider to resize all rows.</summary>
    public double NameColumnWidth
    {
        get => _nameColumnWidth;
        set
        {
            if (!double.IsFinite(value) || value < 64 || value > 220) throw new ArgumentOutOfRangeException(nameof(value));
            if (_nameColumnWidth == value) return;
            _nameColumnWidth = value; ResizeColumns();
        }
    }
    public string Filter
    {
        get => _filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length > 512) throw new ArgumentException("Property search is limited to 512 characters.", nameof(value));
            var next = value.Trim();
            if (string.Equals(next, _filter, StringComparison.OrdinalIgnoreCase)) return;
            _filter = next; Invalidate();
        }
    }
    public IReadOnlyList<string> VisiblePropertyPaths => OrderedRows().Where(r => r.Host.Visibility == Visibility.Visible).Select(r => r.Info.Path).ToArray();
    public PropertyGridControl()
    {
        RequestedTheme = ElementTheme.Light; Content = _body; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SizeChanged += (_, _) => ResizeColumns();
    }
    public void Add(string category, string name, FrameworkElement editor, string description = "", bool fullWidth = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(editor); ArgumentNullException.ThrowIfNull(description);
        if (_rows.Count >= MaximumRows) throw new InvalidOperationException("Property sheet exceeds its 512-row limit.");
        if (_byName.ContainsKey((category, name)) || _editors.Contains(editor))
            throw new ArgumentException("Property names must be unique within a category, and editors cannot be shared between rows.");
        if (editor.Parent != null || VisualTreeHelper.GetParent(editor) != null) throw new ArgumentException("Editor already belongs to a visual parent.", nameof(editor));
        if (!_sections.TryGetValue(category, out var section))
        {
            var label = new TextBlock { Text = category, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var arrow = new Viewbox { Child = new SymbolIcon(Symbol.Forward), Width = 12, Height = 12, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5), RenderTransform = new RotateTransform { Angle = 90 } };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            heading.Children.Add(arrow); heading.Children.Add(label);
            var button = new Button { Content = heading, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 27, Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(0), Background = Fill(225), BorderThickness = new Thickness(0) };
            AutomationProperties.SetName(button, "Property group " + category);
            var rows = new StackPanel(); var host = new StackPanel(); host.Children.Add(button); host.Children.Add(rows);
            section = new Section(host, button, arrow, rows); _sections.Add(category, section);
            var captured = section; button.Click += (_, _) => { captured.Expanded = !captured.Expanded; Invalidate(); };
        }
        var hostRow = new Grid { MinHeight = 28, Background = Fill(_rows.Count % 2 == 0 ? (byte)250 : (byte)245),
            BorderBrush = Fill(220), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(5, 1, 2, 1) };
        if (!fullWidth)
        {
            hostRow.ColumnDefinitions.Add(new() { Width = new GridLength(EffectiveNameWidth()) }); hostRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0) };
            ToolTipService.SetToolTip(label, description.Length == 0 ? name : name + "\n" + description); hostRow.Children.Add(label);
            var divider = new Thumb { Width = 5, HorizontalAlignment = HorizontalAlignment.Right, Background = Fill(225) };
            AutomationProperties.SetName(divider, "Resize property name column");
            ToolTipService.SetToolTip(divider, "Drag to resize property names");
            divider.DragDelta += (_, e) => NameColumnWidth = Math.Clamp(NameColumnWidth + e.HorizontalChange, 64, 220);
            hostRow.Children.Add(divider);
        }
        Grid.SetColumn(editor, fullWidth ? 0 : 1);
        AutomationProperties.SetName(editor, category + "." + name);
        if (description.Length != 0) AutomationProperties.SetHelpText(editor, description);
        editor.HorizontalAlignment = HorizontalAlignment.Stretch; hostRow.Children.Add(editor);
        var row = new Row(new(category, name, description), editor, hostRow, fullWidth);
        _rows.Add(row); section.Items.Add(row); _byName.Add((category, name), row); _editors.Add(editor); _alphabetical = null;
        hostRow.GotFocus += (_, _) => SelectRow(row);
        hostRow.Tapped += (_, _) => SelectRow(row);
        Invalidate(layout: true);
    }
    public FrameworkElement Editor(string category, string name) => Find(category, name).Editor;
    public Button GroupHeader(string category) => _sections.TryGetValue(category, out var s) ? s.Button : throw new ArgumentException("Unknown property group: " + category);
    public void SelectProperty(string category, string name) => SelectRow(Find(category, name));
    private Row Find(string category, string name) => _byName.TryGetValue((category, name), out var row) ? row : throw new ArgumentException("Unknown property: " + category + "." + name);
    private void SelectRow(Row row)
    {
        // Delayed focus/tap from a detached editor must not revive released metadata.
        if (!_byName.TryGetValue((row.Info.Category, row.Info.Name), out var current) || !ReferenceEquals(current, row) || ReferenceEquals(_selected, row)) return;
        if (_selected is { } prior) prior.Host.BorderBrush = Fill(220);
        _selected = row; row.Host.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 185, 154, 0));
        SelectedPropertyChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Clear()
    {
        foreach (var row in _rows) row.Host.Children.Clear();
        foreach (var section in _sections.Values) section.Rows.Children.Clear();
        _body.Children.Clear(); _sections.Clear(); _rows.Clear(); _byName.Clear(); _editors.Clear(); _alphabetical = null;
        _layoutDirty = _dirty = false;
        if (_selected != null) { _selected = null; SelectedPropertyChanged?.Invoke(this, EventArgs.Empty); }
    }
    public IDisposable BeginUpdate() { _updateDepth++; return new UpdateScope(this); }
    private void Invalidate(bool layout = false)
    {
        _dirty = true; _layoutDirty |= layout;
        if (_updateDepth == 0) Flush();
    }
    private void Flush()
    {
        if (_layoutDirty) RebuildLayout();
        if (_dirty) ApplyFilter();
        _layoutDirty = _dirty = false;
    }
    private IEnumerable<Row> OrderedRows() => _sortMode == PropertyGridSortMode.Alphabetical ?
        _alphabetical ??= _rows.OrderBy(r => r.Info.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Info.Category, StringComparer.Ordinal).ToArray() :
        _sections.Values.SelectMany(s => s.Items);
    private void RebuildLayout()
    {
        // Reparent row hosts only; editors and their drafts/event handlers are retained.
        foreach (var section in _sections.Values) section.Rows.Children.Clear();
        _body.Children.Clear();
        if (_sortMode == PropertyGridSortMode.Alphabetical)
            foreach (var row in OrderedRows()) _body.Children.Add(row.Host);
        else
            foreach (var section in _sections.Values)
            {
                foreach (var row in section.Items) section.Rows.Children.Add(row.Host);
                _body.Children.Add(section.Host);
            }
        LayoutPasses++;
    }
    private void ApplyFilter()
    {
        FilterPasses++;
        foreach (var section in _sections.Values)
        {
            var visible = 0;
            foreach (var row in section.Items)
            {
                var match = _filter.Length == 0 || row.Info.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                    row.Info.Category.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.Info.Description.Contains(_filter, StringComparison.OrdinalIgnoreCase);
                row.Host.Visibility = match && (_sortMode == PropertyGridSortMode.Alphabetical || section.Expanded || _filter.Length != 0) ? Visibility.Visible : Visibility.Collapsed;
                if (match) visible++;
            }
            section.Host.Visibility = visible == 0 ? Visibility.Collapsed : Visibility.Visible;
            ((RotateTransform)section.Arrow.RenderTransform).Angle = section.Expanded || _filter.Length != 0 ? 90 : 0;
            AutomationProperties.SetHelpText(section.Button, section.Expanded ? "Expanded" : "Collapsed");
        }
    }
    private double EffectiveNameWidth() => ActualWidth > 0 ? Math.Min(_nameColumnWidth, Math.Max(64, ActualWidth - 87)) : _nameColumnWidth;
    private void ResizeColumns()
    {
        var width = EffectiveNameWidth();
        foreach (var row in _rows)
            if (!row.FullWidth && row.Host.ColumnDefinitions[0].Width.Value != width) row.Host.ColumnDefinitions[0].Width = new GridLength(width);
    }
    private sealed class UpdateScope(PropertyGridControl owner) : IDisposable
    {
        private PropertyGridControl? _owner = owner;
        public void Dispose()
        {
            var grid = Interlocked.Exchange(ref _owner, null);
            if (grid != null && --grid._updateDepth == 0) grid.Flush();
        }
    }
    private static SolidColorBrush Fill(byte v) => new(Windows.UI.Color.FromArgb(255, v, v, v));
    private sealed record Row(PropertyGridSelection Info, FrameworkElement Editor, Grid Host, bool FullWidth);
    private sealed class Section(StackPanel host, Button button, Viewbox arrow, StackPanel rows)
    {
        public StackPanel Host { get; } = host;
        public Button Button { get; } = button;
        public Viewbox Arrow { get; } = arrow;
        public StackPanel Rows { get; } = rows;
        public bool Expanded { get; set; } = true;
        public List<Row> Items { get; } = [];
    }
}
