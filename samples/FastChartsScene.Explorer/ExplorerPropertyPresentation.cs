using FastChartsScene.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private readonly Grid _inspectorPane = new();
    private readonly TextBlock _propertyHelpTitle = new() { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _propertyHelpText = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 58 };
    private Border _propertyHelp = null!;

    private void InitializePropertyPresentation()
    {
        _inspectorPane.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _inspectorPane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _inspectorPane.Children.Add(_inspectorScroll);
        var text = new StackPanel { Spacing = 3 }; text.Children.Add(_propertyHelpTitle); text.Children.Add(_propertyHelpText);
        _propertyHelp = new Border { Child = text, Padding = new Thickness(7), MinHeight = 65,
            RequestedTheme = ElementTheme.Light, Background = Brush("E8E8E8"), BorderBrush = Brush("BDBDBD"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(_propertyHelp, 1); _inspectorPane.Children.Add(_propertyHelp);
        _propertyGrid.SelectedPropertyChanged += (_, _) =>
        {
            var selected = _propertyGrid.SelectedProperty;
            _propertyHelpTitle.Text = selected?.Path ?? "Properties Editor";
            _propertyHelpText.Text = selected == null ? "Select a property for its description. Drag a row divider to resize the name column." :
                selected.Description.Length == 0 ? "Edit the selected object's " + selected.Name + " property." : selected.Description;
        };
        _propertyHelpTitle.Text = "Properties Editor";
        _propertyHelpText.Text = "Select a property for its description. Drag a row divider to resize the name column.";
    }
    private FrameworkElement PropertyPresentationToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var categorized = Compact(Button("Categorized properties", () => _propertyGrid.SortMode = PropertyGridSortMode.Categorized));
        var alphabetical = Compact(Button("Alphabetical properties", () => _propertyGrid.SortMode = PropertyGridSortMode.Alphabetical));
        categorized.Content = "Categories"; alphabetical.Content = "A–Z";
        ToolTipService.SetToolTip(categorized, "Group properties by category");
        ToolTipService.SetToolTip(alphabetical, "Sort all properties alphabetically");
        panel.Children.Add(categorized); panel.Children.Add(alphabetical);
        return panel;
    }
    private static string DescribeProperty(string category, string name) => (category, name) switch
    {
        ("Chart", "Title") => "Main chart title. Text edits commit when focus leaves the editor; Escape restores the current value.",
        ("Chart", "Subtitle") => "Text below the chart title. Stored in the chart document and included in image exports.",
        ("Chart", "Backend") => "Auto uses the GPU backend when available; Skia selects the explicit fallback. This is a host setting, not chart data.",
        ("Chart", "AxisLayout") => "Layer axes in one plot or stack vertical panes. Segmented layout is available only when the document defines segments.",
        ("Axis", "Selected axis") => "Choose which axis to edit. Pending drafts are reset when switching axes; values never transfer to the new axis.",
        ("Axis", "Scale") => "Linear, logarithmic, date/time or category coordinates. A logarithmic axis requires a strictly positive range.",
        ("Axis", "Minimum") => "Draft lower bound. Apply range validates and commits both bounds together; Reset draft restores the viewport.",
        ("Axis", "Maximum") => "Draft upper bound. Must be finite and greater than Minimum, with a finite positive span.",
        ("Axis", "Weight") => "Positive relative size of this axis's pane when axes are stacked.",
        ("Axis", "Side") => "Near or far side placement of this axis in the plot.",
        ("Series", "Selected series") => "Only this series's editors are constructed. Switching the selection does not change chart data.",
        ("Series", "Color") => "Series color as RGB or RGBA hexadecimal. Merely focusing the field never rounds the stored color.",
        ("Series", "XAxis") or ("Series", "YAxis") => "Bind this series to an existing axis by its identifier.",
        ("Series", "CursorTrackEnabled") => "Include this series in supported cursor queries without hiding or changing its data. Saved with the chart.",
        ("Series", "CursorSnapMode") => "NearestPoint tracks within a circular pixel radius. NearestX follows the original visible candle nearest horizontally in this pane, then vertically for duplicate X. It does not interpolate prices.",
        ("Series", "CursorField") => "Original Open, High, Low or Close price tracked by the financial cursor. Close is the default; volume remains a readout field.",
        ("Series", "PointCount") => "Read-only original source count, not the reduced number of rendered vertices.",
        (_, "Title") => "Displayed title of the selected object. Escape discards an uncommitted text draft.",
        (_, "Units") => "Units displayed with the selected axis title.",
        (_, "Inverted") => "Reverse this axis's direction without changing its numeric range.",
        (_, "StrokeWidth") => "Nonnegative line width in device-independent pixels.",
        (_, "Visible") => "Show or hide this series without removing its data.",
        _ => "Edit the selected object's " + name + " property. Invalid values leave the model unchanged."
    };
}
