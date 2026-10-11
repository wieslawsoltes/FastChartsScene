using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Examples;
using FastChartsScene.Rendering;
using FastChartsScene.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private readonly CatalogBrowserModel _catalog = new(ExampleCatalog.All.Select(ToEntry));
    private readonly CatalogBrowserControl _gallery = new();
    private readonly Grid _center = new();
    private readonly TextBlock _exampleTitle = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock _exampleSummary = new() { FontSize = 11, Opacity = .75, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _exampleTags = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private readonly TextBox _propertySearch = new() { PlaceholderText = "Search properties", FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Button _star = new() { Content = "☆", FontSize = 22, Padding = new Thickness(8, 1, 8, 1), Foreground = Brush("E5BF00") };
    private ScrollViewer _toolbarScroll = null!;
    private ScrollViewer _inspectorScroll = null!;
    private Grid _brand = null!;
    private StackPanel _identity = null!;
    private TextBlock _badge = null!;
    private bool _detailsVisible, _restoringGallerySettings;
    public bool GalleryIsOpen => _gallery.Visibility == Visibility.Visible;
    internal CatalogBrowserControl Gallery => _gallery;

    private static CatalogEntry ToEntry(ChartExample example)
    {
        List<string> tags = [example.Category == "3D charts" ? "3D" : "2D"];
        if (example.Category is "XY charts" or "Axes & layout" or "Intensity & contours" or "Financial" or "Signal processing") tags.Add("ViewXY");
        if (example.Category == "3D charts") tags.Add("View3D");
        if (example.Id is "signals" or "streaming") tags.Add("Realtime");
        if (example.Id == "million") tags.Add("Performance");
        if (example.Category == "Intensity & contours" || example.Id == "spectrogram") tags.Add("Intensity");
        if (example.Id is "spectrum" or "spectrogram") tags.Add("FFT");
        if (example.Id is "log" or "color-scales") tags.Add("Logarithmic");
        if (example.Id is "stacked" or "segmented" or "intensity-panes") tags.Add("Multi-axis");
        if (example.Category == "Financial") tags.Add("Financial");
        if (example.Category == "Circular charts") tags.Add(example.Id == "polar" ? "Polar" : "Smith");
        return new(example.Id, example.Title, example.Category, example.Description, tags);
    }
    private void BuildShell()
    {
        Background = Brush("303030"); _inspector.Spacing = 8; _inspector.Padding = new Thickness(10);
        _root.RowDefinitions.Add(new() { Height = new GridLength(46) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(66) });
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(24) });
        _brand = new Grid { Background = Brush("303030"), Padding = new Thickness(14, 0, 14, 0) };
        _brand.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _brand.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        _identity.Children.Add(new SymbolIcon(Symbol.ShowResults) { Foreground = Brush("E5BF00"), Width = 25, Height = 25 });
        _identity.Children.Add(new TextBlock { Text = "FastChartsScene", FontSize = 21, FontStyle = Windows.UI.Text.FontStyle.Italic, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _identity.Children.Add(new TextBlock { Text = "│ .NET Interactive Examples", FontSize = 18, Foreground = Brush("E5BF00"), VerticalAlignment = VerticalAlignment.Center });
        _brand.Children.Add(_identity);
        _badge = new TextBlock { Text = "Independent implementation · Preview", FontSize = 10, Opacity = .65, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(_badge, 1); _brand.Children.Add(_badge); _root.Children.Add(_brand);

        var heading = new Grid { Background = Brush("343434"), Padding = new Thickness(8, 2, 8, 6), ColumnSpacing = 10 };
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(32) }); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var back = new Button { Content = new SymbolIcon(Symbol.Back), Background = Brush("E5BF00"), Foreground = Brush("222222"), Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(back, "Back to gallery"); ToolTipService.SetToolTip(back, "Back to example gallery"); back.Click += (_, _) => ShowGallery(); heading.Children.Add(back);
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; titleRow.Children.Add(_exampleTitle); titleRow.Children.Add(_exampleTags);
        var titleScroll = new ScrollViewer { Content = titleRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        text.Children.Add(titleScroll); text.Children.Add(_exampleSummary); Grid.SetColumn(text, 1); heading.Children.Add(text);
        AutomationProperties.SetName(_star, "Favorite current example"); ToolTipService.SetToolTip(_star, "Favorite current example");
        _star.Click += (_, _) => { if (Current.Id != "document") _catalog.SetFavorite(Current.Id, !_catalog.IsFavorite(Current.Id)); };
        Grid.SetColumn(_star, 2); heading.Children.Add(_star); Grid.SetRow(heading, 1); _root.Children.Add(heading);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(8, 4, 8, 4) };
        toolbar.Children.Add(Button("Examples", () => { if (ActualWidth >= 680) ShowGallery(); }));
        _play.Click += (_, _) => Execute(() => SetStreaming(!IsStreaming)); toolbar.Children.Add(_play);
        toolbar.Children.Add(Button("Fit", () => Execute(() => Chart.Interaction?.ResetView())));
        toolbar.Children.Add(Button("Undo", () => Execute(() => Chart.Interaction?.Undo()))); toolbar.Children.Add(Button("Redo", () => Execute(() => Chart.Interaction?.Redo())));
        toolbar.Children.Add(Button("PNG", () => Export("png"))); toolbar.Children.Add(Button("CSV", () => Export("csv"))); toolbar.Children.Add(Button("SVG", () => Export("svg")));
        toolbar.Children.Add(Button("All data CSV", () => Export("allcsv"))); toolbar.Children.Add(Button("Save chart", SaveDocument)); toolbar.Children.Add(Button("Open chart", OpenDocument));
        toolbar.Children.Add(Button("Frame JSON", () => Export("json")));
        toolbar.Children.Add(Button("Inspector", () => { if (ActualWidth >= 1100) { _inspectorVisible = !_inspectorVisible; Responsive(); } }));
        toolbar.Children.Add(Button("Details", () => { _detailsVisible = !_detailsVisible; Responsive(); }));
        foreach (var button in toolbar.Children.OfType<Button>())
        { button.MinHeight = 27; button.Padding = new Thickness(8, 3, 8, 3); button.FontSize = 11; button.CornerRadius = new CornerRadius(1); button.Foreground = Brush("F2CC00"); ToolTipService.SetToolTip(button, button.Content); }
        _toolbarScroll = new ScrollViewer { Content = toolbar, Background = Brush("454545"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        Grid.SetRow(_toolbarScroll, 2); _root.Children.Add(_toolbarScroll);

        _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(0) }); _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _workspace.ColumnDefinitions.Add(new() { Width = new GridLength(274) });
        _workspace.Children.Add(new Border());
        _center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); _center.RowDefinitions.Add(new() { Height = new GridLength(0) }); _center.Children.Add(Chart);
        var tabs = new Pivot { Background = Brush("303030"), Margin = new Thickness(1) };
        tabs.Items.Add(new PivotItem { Header = "Overview", Content = new ScrollViewer { Content = _description } }); tabs.Items.Add(new PivotItem { Header = "Data", Content = _data }); tabs.Items.Add(new PivotItem { Header = "C# usage", Content = _usage });
        Grid.SetRow(tabs, 1); _center.Children.Add(tabs); Grid.SetColumn(_center, 1); _workspace.Children.Add(_center);
        _inspectorScroll = new ScrollViewer { Content = _inspector, Background = Brush("F2F2F2"), RequestedTheme = ElementTheme.Light, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
        Grid.SetColumn(_inspectorScroll, 2); _workspace.Children.Add(_inspectorScroll); Grid.SetRow(_workspace, 3); _root.Children.Add(_workspace);
        _status.Margin = new Thickness(10, 3, 10, 2); _status.Foreground = Brush("C6B343"); Grid.SetRow(_status, 4); _root.Children.Add(_status);
        _gallery.PreviewProvider = CreatePreview; _gallery.SetModel(_catalog, "signals"); _gallery.OpenRequested += (_, entry) => Execute(() => SelectExample(entry.Id));
        Grid.SetRow(_gallery, 1); Grid.SetRowSpan(_gallery, 3); _root.Children.Add(_gallery);
        _gallery.ExportFavoritesRequested += (_, _) => ExportFavoritesFile();
        _gallery.ImportFavoritesRequested += (_, _) => ImportFavoritesFile();
        _catalog.FavoritesChanged += (_, _) => { UpdateExampleHeader(); SaveGallerySettings(); };
        AutomationProperties.SetName(_propertySearch, "Search properties");
        _propertySearch.TextChanged += (_, _) => FilterProperties();
        Content = _root; RestoreGallerySettings();
    }
    private static async Task<ImageSource?> CreatePreview(CatalogEntry entry, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var document = ExampleCatalog.Find(entry.Id).Create();
        var compiler = new SceneCompiler(); var frame = compiler.Compile(document, 560, 350);
        byte[] bytes; using (var renderer = new SkiaFrameRenderer()) bytes = renderer.ExportPng(frame, .5);
        compiler.ClearCache(); cancellation.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0); var image = new BitmapImage(); await image.SetSourceAsync(stream); cancellation.ThrowIfCancellationRequested(); return image;
    }
    private void UpdateExampleHeader()
    {
        _exampleTitle.Text = Current.Title; _exampleSummary.Text = Current.Description;
        _exampleTags.Children.Clear(); _star.IsEnabled = Current.Id != "document";
        _star.Content = _star.IsEnabled && _catalog.IsFavorite(Current.Id) ? "★" : "☆";
        if (Current.Id == "document") return;
        foreach (var tag in _catalog.Find(Current.Id).Tags)
        {
            var button = new Button { Content = tag, Padding = new Thickness(4, 1, 4, 1), MinHeight = 20, FontSize = 10, Foreground = Brush("E5BF00"), Background = Brush("272727") };
            button.Click += (_, _) => { ShowGallery(); _gallery.SetSearch(tag); }; _exampleTags.Children.Add(button);
        }
    }
    public void ShowGallery(bool visible = true)
    {
        if (visible) { SetStreaming(false); _gallery.Visibility = Visibility.Visible; _gallery.RefreshPreviews(); _gallery.FocusSearch(); }
        else { _gallery.Visibility = Visibility.Collapsed; _gallery.SuspendPreviews(); SaveGallerySettings(); }
    }
    private void FilterProperties()
    {
        var query = _propertySearch.Text.Trim();
        foreach (var item in _inspector.Children.OfType<FrameworkElement>())
        {
            if (ReferenceEquals(item, _propertySearch) || item is TextBlock) continue;
            var text = item switch { TextBox box => box.Header?.ToString(), ComboBox box => box.Header?.ToString(), NumberBox box => box.Header?.ToString(), Slider slider => slider.Header?.ToString(), CheckBox box => box.Content?.ToString(), Button button => button.Content?.ToString(), IntensityScaleEditor => "Intensity palette color range scale missing value custom", _ => "" };
            item.Visibility = query.Length == 0 || (text ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void SaveGallerySettings()
    {
        if (_restoringGallerySettings) return;
        try { ApplicationData.Current.LocalSettings.Values["FastChartsScene.Favorites.v1"] = _catalog.ExportFavorites(); ApplicationData.Current.LocalSettings.Values["FastChartsScene.GalleryMode"] = _gallery.IsTileView ? "Tiles" : "List"; }
        catch (Exception error) { Console.Error.WriteLine("Gallery settings were not persisted: " + error.Message); }
    }
    private void RestoreGallerySettings()
    {
        _restoringGallerySettings = true;
        try
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.TryGetValue("FastChartsScene.Favorites.v1", out var value) && value is string json) _catalog.ImportFavorites(json);
            if (settings.TryGetValue("FastChartsScene.GalleryMode", out var mode) && mode is string text) _gallery.ChangeView(text != "List");
        }
        catch (Exception error) { Console.Error.WriteLine("Ignoring invalid saved gallery settings: " + error.Message); }
        finally { _restoringGallerySettings = false; }
    }
    private async void ExportFavoritesFile()
    {
        try { await SaveFile(System.Text.Encoding.UTF8.GetBytes(_catalog.ExportFavorites()), "FastChartsScene-favorites.json", "application/json", ".json"); LastCommandError = null; }
        catch (Exception error) { LastCommandError = error.Message; }
        UpdateStatus();
    }
    private async void ImportFavoritesFile()
    {
        try
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.List }; picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync(); if (file == null) return;
            if ((await file.GetBasicPropertiesAsync()).Size > CatalogBrowserModel.MaximumFavoritesFileLength) throw new FormatException("Favorites file exceeds 64 KiB.");
            _catalog.ImportFavorites(await FileIO.ReadTextAsync(file)); LastCommandError = null;
        }
        catch (Exception error) { LastCommandError = error.Message; }
        UpdateStatus();
    }
    internal string ExportFavorites() => _catalog.ExportFavorites();
    internal void ImportFavorites(string json) => _catalog.ImportFavorites(json);
}
