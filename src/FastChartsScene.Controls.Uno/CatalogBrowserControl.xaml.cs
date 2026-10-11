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
    public Symbol FavoriteSymbol => _favorite ? Symbol.SolidStar : Symbol.OutlineStar;
    internal void SetFavorite(bool value) { if (_favorite == value) return; _favorite = value; Changed(nameof(FavoriteGlyph)); Changed(nameof(FavoriteSymbol)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>Reusable virtualized tile/list catalog. The host supplies immutable metadata and a
/// thumbnail factory. No examples, files, navigation destinations or network APIs are hardcoded.</summary>
public sealed partial class CatalogBrowserControl : UserControl, IDisposable
{
    public const int MaximumCachedPreviews = 32;
    public const int MaximumPendingPreviews = 64;
    private CatalogBrowserModel? _model;
    private readonly Dictionary<string, CatalogCard> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToggleButton> _tagButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly Queue<CatalogCard> _requests = new();
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private readonly LinkedList<CatalogCard> _previewLru = new();
    private CancellationTokenSource _cancel = new();
    private IReadOnlyList<CatalogEntry>? _lastEntries;
    private bool _sync, _disposed, _processing, _modelSubscribed;
    private int _generation;
    private string? _pendingScroll;
    public CatalogBrowserModel? Model => _model;
    public CatalogEntry? SelectedEntry { get; private set; }
    public bool IsTileView { get; private set; } = true;
    public string SearchText => Search.Text;
    public IReadOnlyList<string> VisibleIds => _lastEntries?.Select(e => e.Id).ToArray() ?? [];
    public FrameworkElement? EntryContainer(string id) => (IsTileView ? (ListViewBase)Tiles : List).ContainerFromItem(_cards[id]) as FrameworkElement;
    public void BringEntryIntoView(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_cards.ContainsKey(id) || _lastEntries?.Any(e => e.Id == id) != true) throw new ArgumentException("Entry is not in the current view: " + id, nameof(id));
        _pendingScroll = id;
        DispatcherQueue.TryEnqueue(ApplyPendingScroll);
    }
    private void ApplyPendingScroll()
    {
        if (_disposed || !IsLoaded || Visibility != Visibility.Visible || _pendingScroll is not { } id) return;
        var view = IsTileView ? (ListViewBase)Tiles : List;
        if (view.ActualWidth <= 1 || view.ActualHeight <= 1 || view.ItemsPanelRoot == null) return;
        _pendingScroll = null; // ScrollIntoView may trigger layout; never re-enter the same request.
        if (_lastEntries?.Any(e => e.Id == id) == true) view.ScrollIntoView(_cards[id]);
    }
    public int CachedPreviews => _previewLru.Count;
    public int PendingPreviews => _requests.Count;
    public int GeneratedPreviews { get; private set; }
    public int PreviewErrors { get; private set; }
    private Func<CatalogEntry, CancellationToken, Task<ImageSource?>>? _previewProvider;
    public Func<CatalogEntry, CancellationToken, Task<ImageSource?>>? PreviewProvider
    {
        get => _previewProvider;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(_previewProvider, value)) return;
            _previewProvider = value; InvalidatePreviews();
        }
    }
    public event EventHandler<CatalogEntry>? OpenRequested;
    public event EventHandler? SelectionUpdated;
    public event EventHandler? ExportFavoritesRequested;
    public event EventHandler? ImportFavoritesRequested;
    public CatalogBrowserControl()
    {
        InitializeComponent();
        // ListViewBase handles Enter as part of selection. Observe that routed key as
        // well, so the catalog can perform its documented Open action after selection.
        Tiles.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(EntryKeyDown), true);
        List.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(EntryKeyDown), true);
        LayoutUpdated += (_, _) => ApplyPendingScroll();
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };
        SizeChanged += (_, _) => { var compact = ActualWidth < 850; PreviewColumn.Width = new GridLength(compact ? 0 : 300); PreviewPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; };
        Unloaded += (_, _) => { if (!_disposed) { DetachModel(); SuspendPreviews(); } };
        Loaded += (_, _) => { if (!_disposed && _model != null) { AttachModel(); ApplyFilter(); RefreshPreviews(); } };
    }
    public void SetModel(CatalogBrowserModel model, string? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(model); ObjectDisposedException.ThrowIf(_disposed, this);
        var selection = selectedId == null ? model.Entries.FirstOrDefault() : model.Find(selectedId);
        DetachModel();
        SuspendPreviews(); _pendingScroll = null; _model = model; if (IsLoaded) AttachModel();
        _cards.Clear(); _previewLru.Clear(); _failed.Clear(); _lastEntries = null; _tags.Clear(); _tagButtons.Clear(); TagButtons.Children.Clear();
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
        SelectedEntry = selection;
        ApplyFilter();
    }
    public void SelectEntry(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model == null) return;
        SelectedEntry = _model.Find(id); UpdateSelected();
        _sync = true; Tiles.SelectedItem = List.SelectedItem = _cards[id]; _sync = false;
    }
    public void SetSearch(string query) { ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(query); if (query.Length > 512) throw new ArgumentException("Search text is limited to 512 characters.", nameof(query)); Search.Text = query; _debounce.Stop(); ApplyFilter(); }
    public void ResetFilters()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            SuspendPreviews(); _lastEntries = entries; _sync = true;
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
        FavoriteSelectedIcon.Symbol = entry != null && _model!.IsFavorite(entry.Id) ? Symbol.SolidStar : Symbol.OutlineStar;
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
        if (_disposed || _sync || sender is not ListViewBase { SelectedItem: CatalogCard card }) return;
        SelectEntry(card.Id);
    }
    private void ExportFavoritesClicked(object sender, RoutedEventArgs args) => ExportFavoritesRequested?.Invoke(this, EventArgs.Empty);
    private void ImportFavoritesClicked(object sender, RoutedEventArgs args) => ImportFavoritesRequested?.Invoke(this, EventArgs.Empty);
    public void FocusSearch() { ObjectDisposedException.ThrowIf(_disposed, this); Search.Focus(FocusState.Programmatic); }
    public void RefreshPreviews()
    {
        if (_disposed || !IsLoaded || Visibility != Visibility.Visible) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !IsLoaded || Visibility != Visibility.Visible) return;
            var view = IsTileView ? (ListViewBase)Tiles : List;
            // Enumerate realized containers, not a prefix of source indices. Scrolled views
            // beyond item 128 must refresh too, without an O(catalog size) walk.
            if (view.ItemsPanelRoot is { } panel)
                foreach (var child in panel.Children)
                    if (child is SelectorItem { Content: CatalogCard card }) RequestPreview(card);
            if (SelectedEntry != null) RequestPreview(_cards[SelectedEntry.Id]);
        });
    }
    private void OpenClicked(object sender, RoutedEventArgs args) { if (SelectedEntry != null) OpenRequested?.Invoke(this, SelectedEntry); }
    private void OpenDoubleTapped(object sender, DoubleTappedRoutedEventArgs args) { OpenClicked(sender, new RoutedEventArgs()); args.Handled = true; }
    private void EntryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_disposed || Visibility != Visibility.Visible || args.Key != VirtualKey.Enter) return;
        // Enter on an embedded favorite button belongs to that button, not navigation.
        for (var node = args.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, sender); node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase) return;
        OpenClicked(sender, new RoutedEventArgs()); args.Handled = true;
    }
    private void FavoriteClicked(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string id } && _model != null) _model.SetFavorite(id, !_model.IsFavorite(id)); }
    private void FavoriteSelectedClicked(object sender, RoutedEventArgs args)
    { if (SelectedEntry != null && _model != null) _model.SetFavorite(SelectedEntry.Id, !_model.IsFavorite(SelectedEntry.Id)); }
    private void TilesClicked(object sender, RoutedEventArgs args) => ChangeView(true);
    private void ListClicked(object sender, RoutedEventArgs args) => ChangeView(false);
    public void ChangeView(bool tiles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SuspendPreviews(); IsTileView = tiles; Tiles.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed; List.Visibility = tiles ? Visibility.Collapsed : Visibility.Visible;
        // A previously collapsed virtualizing panel may not have a layout yet. Keep the
        // request until its real panel is arranged instead of dropping early navigation.
        if (SelectedEntry != null) _pendingScroll = SelectedEntry.Id;
        DispatcherQueue.TryEnqueue(ApplyPendingScroll);
        RefreshPreviews(); SelectionUpdated?.Invoke(this, EventArgs.Empty);
    }
    private void ContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is CatalogCard card) RequestPreview(card);
    }
    private void RequestPreview(CatalogCard? card)
    {
        if (card == null || _disposed || PreviewProvider == null || _failed.Contains(card.Id) || Visibility != Visibility.Visible || !IsLoaded) return;
        if (card.Preview != null)
        {
            _previewLru.Remove(card); _previewLru.AddLast(card); return;
        }
        if (!_pending.Add(card.Id)) return;
        while (_requests.Count >= MaximumPendingPreviews) _pending.Remove(_requests.Dequeue().Id);
        _requests.Enqueue(card);
        if (!_processing) { _processing = true; if (!DispatcherQueue.TryEnqueue(ProcessPreviews)) _processing = false; }
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
                        while (_previewLru.Count > MaximumCachedPreviews)
                        {
                            var first = _previewLru.First!.Value; _previewLru.RemoveFirst();
                            if (first.Id == SelectedEntry?.Id) _previewLru.AddLast(first); else first.Preview = null;
                        }
                    }
                    else _failed.Add(card.Id);
                    if (SelectedEntry?.Id == card.Id) SelectedImage.Source = image;
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { if (generation == _generation) { card.PreviewMessage = "Preview unavailable"; _failed.Add(card.Id); PreviewErrors++; Console.Error.WriteLine("Catalog preview " + card.Id + ": " + error.Message); } }
                finally { if (generation == _generation) _pending.Remove(card.Id); }
            }
        }
        finally { _processing = false; }
    }
    private void AttachModel()
    {
        if (_modelSubscribed || _model == null) return;
        _model.FavoritesChanged += FavoritesChanged; _modelSubscribed = true;
    }
    private void DetachModel()
    {
        if (_modelSubscribed && _model != null) _model.FavoritesChanged -= FavoritesChanged;
        _modelSubscribed = false;
    }
    /// <summary>Clear thumbnail cache and retry prior failures after changing the host's provider/data.
    /// Call on the UI thread. Filtering and view changes retain successful cached images.</summary>
    public void InvalidatePreviews()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); SuspendPreviews(); _previewLru.Clear(); _failed.Clear();
        foreach (var card in _cards.Values) { card.Preview = null; card.PreviewMessage = "Preparing preview…"; }
        SelectedImage.Source = null; RefreshPreviews();
    }
    public void SuspendPreviews()
    {
        if (_disposed) return;
        _generation++; _cancel.Cancel(); _cancel.Dispose(); _cancel = new(); _requests.Clear(); _pending.Clear(); _debounce.Stop();
    }
    public new void Dispose()
    {
        if (_disposed) return; SuspendPreviews(); _disposed = true; _cancel.Dispose();
        DetachModel();
        Tiles.ItemsSource = List.ItemsSource = null; _cards.Clear(); _previewLru.Clear(); SelectedImage.Source = null; SelectedEntry = null; _lastEntries = null; _pendingScroll = null; _failed.Clear(); _previewProvider = null; _model = null;
    }
}
