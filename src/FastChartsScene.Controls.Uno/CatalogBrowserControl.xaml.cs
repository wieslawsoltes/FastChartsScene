using System.ComponentModel;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FastChartsScene.Controls.Uno;

public sealed class CatalogCard(CatalogEntry entry) : INotifyPropertyChanged
{
    public CatalogEntry Entry { get; } = entry;
    public string Id => Entry.Id;
    public string Title => Entry.Title;
    public string Description => Entry.Description;
    public string TagsText => Entry.TagsText;
    private ImageSource? _preview;
    private bool _favorite;
    private string _previewMessage = "Preparing preview…";
    public ImageSource? Preview { get => _preview; internal set { _preview = value; Changed(nameof(Preview)); } }
    public string PreviewMessage { get => _previewMessage; internal set { _previewMessage = value; Changed(nameof(PreviewMessage)); } }
    public string FavoriteGlyph => _favorite ? "★" : "☆";
    internal void SetFavorite(bool value) { if (_favorite == value) return; _favorite = value; Changed(nameof(FavoriteGlyph)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>Reusable virtualized tile/list catalog. The host supplies immutable metadata and a
/// thumbnail factory. No examples, files, navigation destinations or network APIs are hardcoded.</summary>
public sealed partial class CatalogBrowserControl : UserControl, IDisposable
{
    private CatalogBrowserModel? _model;
    private readonly Dictionary<string, CatalogCard> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToggleButton> _tagButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly Queue<CatalogCard> _requests = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly LinkedList<CatalogCard> _previewLru = new();
    private CancellationTokenSource _cancel = new();
    private IReadOnlyList<CatalogEntry>? _lastEntries;
    private bool _sync, _disposed, _processing;
    private int _generation;
    public CatalogBrowserModel? Model => _model;
    public CatalogEntry? SelectedEntry { get; private set; }
    public bool IsTileView { get; private set; } = true;
    public string SearchText => Search.Text;
    public IReadOnlyList<string> VisibleIds => _lastEntries?.Select(e => e.Id).ToArray() ?? [];
    public FrameworkElement? EntryContainer(string id) => (IsTileView ? (ListViewBase)Tiles : List).ContainerFromItem(_cards[id]) as FrameworkElement;
    public void BringEntryIntoView(string id) => (IsTileView ? (ListViewBase)Tiles : List).ScrollIntoView(_cards[id]);
    public int CachedPreviews => _previewLru.Count;
    public int GeneratedPreviews { get; private set; }
    public int PreviewErrors { get; private set; }
    public Func<CatalogEntry, CancellationToken, Task<ImageSource?>>? PreviewProvider { get; set; }
    public event EventHandler<CatalogEntry>? OpenRequested;
    public event EventHandler? SelectionUpdated;
    public event EventHandler? ExportFavoritesRequested;
    public event EventHandler? ImportFavoritesRequested;
    public CatalogBrowserControl()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };
        SizeChanged += (_, _) => { var compact = ActualWidth < 850; PreviewColumn.Width = new GridLength(compact ? 0 : 300); PreviewPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; };
        Unloaded += (_, _) => SuspendPreviews();
        Loaded += (_, _) => { if (_model != null) { ApplyFilter(); RefreshPreviews(); } };
    }
    public void SetModel(CatalogBrowserModel model, string? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(model); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model != null) _model.FavoritesChanged -= FavoritesChanged;
        SuspendPreviews(); _model = model; _model.FavoritesChanged += FavoritesChanged;
        _cards.Clear(); _previewLru.Clear(); _lastEntries = null; _tags.Clear(); _tagButtons.Clear(); TagButtons.Children.Clear();
        foreach (var entry in model.Entries) { var card = new CatalogCard(entry); card.SetFavorite(model.IsFavorite(entry.Id)); _cards.Add(entry.Id, card); }
        _sync = true;
        Category.ItemsSource = new[] { "All examples" }.Concat(model.Categories).ToArray(); Category.SelectedIndex = 0;
        foreach (var tag in model.Tags)
        {
            var button = new ToggleButton { Content = tag, Tag = tag, Padding = new Thickness(8, 3, 8, 3), MinHeight = 28, FontSize = 11 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Tag " + tag);
            button.Click += (_, _) => { if (button.IsChecked == true) _tags.Add(tag); else _tags.Remove(tag); ApplyFilter(); };
            _tagButtons.Add(tag, button); TagButtons.Children.Add(button);
        }
        _sync = false;
        SelectedEntry = selectedId == null ? model.Entries.FirstOrDefault() : model.Find(selectedId);
        ApplyFilter();
    }
    public void SelectEntry(string id)
    {
        if (_model == null) return;
        SelectedEntry = _model.Find(id); UpdateSelected();
        _sync = true; Tiles.SelectedItem = List.SelectedItem = _cards[id]; _sync = false;
    }
    public void SetSearch(string query) { Search.Text = query; _debounce.Stop(); ApplyFilter(); }
    public void ResetFilters()
    {
        _sync = true; Search.Text = ""; Category.SelectedIndex = 0; FavoritesOnly.IsChecked = false; TagMode.SelectedIndex = 0;
        _tags.Clear(); foreach (var button in _tagButtons.Values) button.IsChecked = false;
        _sync = false; _debounce.Stop(); ApplyFilter();
    }
    private void ApplyFilter()
    {
        if (_sync || _disposed || _model == null) return;
        var entries = _model.Filter(Search.Text, Category.SelectedIndex <= 0 ? null : Category.SelectedItem as string, _tags, TagMode.SelectedIndex != 1, FavoritesOnly.IsChecked == true);
        foreach (var card in _cards.Values) card.SetFavorite(_model.IsFavorite(card.Id));
        if (!ReferenceEquals(entries, _lastEntries))
        {
            _lastEntries = entries; _sync = true;
            var cards = entries.Select(e => _cards[e.Id]).ToArray(); Tiles.ItemsSource = List.ItemsSource = cards;
            if (SelectedEntry == null || !entries.Any(e => e.Id == SelectedEntry.Id)) SelectedEntry = entries.FirstOrDefault();
            Tiles.SelectedItem = List.SelectedItem = SelectedEntry == null ? null : _cards[SelectedEntry.Id]; _sync = false;
        }
        ResultCount.Text = $"{entries.Count} / {_model.Entries.Count} examples   ·   {_model.FavoriteCount} favorites";
        Empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelected(); RefreshPreviews();
    }
    private void UpdateSelected()
    {
        var entry = SelectedEntry;
        SelectedTitle.Text = entry?.Title ?? "No example selected"; SelectedCategory.Text = entry?.Category ?? "";
        SelectedDescription.Text = entry?.Description ?? "Change the search or filters to see examples.";
        SelectedImage.Source = entry == null ? null : _cards[entry.Id].Preview;
        FavoriteSelected.Content = entry != null && _model!.IsFavorite(entry.Id) ? "★" : "☆";
        Open.IsEnabled = FavoriteSelected.IsEnabled = entry != null; SelectedTags.Children.Clear();
        if (entry != null)
        {
            foreach (var tag in entry.Tags)
            {
                var button = new Button { Content = tag, FontSize = 11, Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
                button.Click += (_, _) => { _tags.Add(tag); if (_tagButtons.TryGetValue(tag, out var toggle)) toggle.IsChecked = true; ApplyFilter(); };
                SelectedTags.Children.Add(button);
            }
            RequestPreview(_cards[entry.Id]);
        }
        SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    private void SearchChanged(object sender, TextChangedEventArgs args) { if (_sync || _model == null) return; _debounce.Stop(); _debounce.Start(); }
    private void FilterChanged(object sender, SelectionChangedEventArgs args) => ApplyFilter();
    private void FavoriteFilterChanged(object sender, RoutedEventArgs args) => ApplyFilter();
    private void ClearClicked(object sender, RoutedEventArgs args) => ResetFilters();
    private void FavoritesChanged(object? sender, EventArgs args) => ApplyFilter();
    private void SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_sync || sender is not ListViewBase { SelectedItem: CatalogCard card }) return;
        SelectEntry(card.Id);
    }
    private void ExportFavoritesClicked(object sender, RoutedEventArgs args) => ExportFavoritesRequested?.Invoke(this, EventArgs.Empty);
    private void ImportFavoritesClicked(object sender, RoutedEventArgs args) => ImportFavoritesRequested?.Invoke(this, EventArgs.Empty);
    public void FocusSearch() => Search.Focus(FocusState.Programmatic);
    public void RefreshPreviews()
    {
        if (_disposed || !IsLoaded || Visibility != Visibility.Visible) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !IsLoaded || Visibility != Visibility.Visible) return;
            var view = IsTileView ? (ListViewBase)Tiles : List;
            foreach (var entry in (_lastEntries ?? []).Take(128))
                if (view.ContainerFromItem(_cards[entry.Id]) is FrameworkElement) RequestPreview(_cards[entry.Id]);
            if (SelectedEntry != null) RequestPreview(_cards[SelectedEntry.Id]);
        });
    }
    private void OpenClicked(object sender, RoutedEventArgs args) { if (SelectedEntry != null) OpenRequested?.Invoke(this, SelectedEntry); }
    private void OpenDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) { OpenClicked(sender, new RoutedEventArgs()); args.Handled = true; }
    private void EntryKeyDown(object sender, KeyRoutedEventArgs args) { if (args.Key == VirtualKey.Enter) { OpenClicked(sender, new RoutedEventArgs()); args.Handled = true; } }
    private void FavoriteClicked(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string id } && _model != null) _model.SetFavorite(id, !_model.IsFavorite(id)); }
    private void FavoriteSelectedClicked(object sender, RoutedEventArgs args)
    { if (SelectedEntry != null && _model != null) _model.SetFavorite(SelectedEntry.Id, !_model.IsFavorite(SelectedEntry.Id)); }
    private void TilesClicked(object sender, RoutedEventArgs args) => ChangeView(true);
    private void ListClicked(object sender, RoutedEventArgs args) => ChangeView(false);
    public void ChangeView(bool tiles)
    {
        IsTileView = tiles; Tiles.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed; List.Visibility = tiles ? Visibility.Collapsed : Visibility.Visible;
        if (SelectedEntry != null) (tiles ? (ListViewBase)Tiles : List).ScrollIntoView(_cards[SelectedEntry.Id]);
        RefreshPreviews(); SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    private void ContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is CatalogCard card) RequestPreview(card);
    }
    private void RequestPreview(CatalogCard? card)
    {
        if (card == null || _disposed || PreviewProvider == null || Visibility != Visibility.Visible || !IsLoaded) return;
        if (card.Preview != null)
        {
            _previewLru.Remove(card); _previewLru.AddLast(card); return;
        }
        if (!_pending.Add(card.Id)) return; _requests.Enqueue(card);
        if (!_processing) { _processing = true; DispatcherQueue.TryEnqueue(ProcessPreviews); }
    }
    private async void ProcessPreviews()
    {
        try
        {
            while (!_disposed && _requests.TryDequeue(out var card))
            {
                var generation = _generation; var token = _cancel.Token;
                try
                {
                    await Task.Delay(16, token); // One bounded request at a time; allow input/presentation between previews.
                    var image = await PreviewProvider!(card.Entry, token);
                    if (_disposed || token.IsCancellationRequested || generation != _generation) continue;
                    card.Preview = image; card.PreviewMessage = image == null ? "No preview available" : ""; GeneratedPreviews++;
                    if (image != null)
                    {
                        _previewLru.Remove(card); _previewLru.AddLast(card);
                        while (_previewLru.Count > 32) { var first = _previewLru.First!.Value; _previewLru.RemoveFirst(); first.Preview = null; }
                    }
                    if (SelectedEntry?.Id == card.Id) SelectedImage.Source = image;
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { if (generation == _generation) { card.PreviewMessage = "Preview unavailable"; PreviewErrors++; Console.Error.WriteLine("Catalog preview " + card.Id + ": " + error.Message); } }
                finally { if (generation == _generation) _pending.Remove(card.Id); }
            }
        }
        finally { _processing = false; }
    }
    public void SuspendPreviews()
    {
        _generation++; _cancel.Cancel(); _cancel.Dispose(); _cancel = new(); _requests.Clear(); _pending.Clear(); _debounce.Stop();
    }
    public new void Dispose()
    {
        if (_disposed) return; _disposed = true; SuspendPreviews(); _cancel.Dispose();
        if (_model != null) _model.FavoritesChanged -= FavoritesChanged;
        Tiles.ItemsSource = List.ItemsSource = null; _cards.Clear(); _previewLru.Clear(); SelectedImage.Source = null; _model = null;
    }
}
