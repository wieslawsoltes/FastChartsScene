using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace FastChartsScene.Core;

/// <summary>Immutable, reusable gallery metadata. No chart, platform or filesystem dependency.</summary>
public sealed class CatalogEntry
{
    public string Id { get; }
    public string Title { get; }
    public string Category { get; }
    public string Description { get; }
    public ReadOnlyCollection<string> Tags { get; }
    public string TagsText { get; }
    internal string SearchText { get; }
    public CatalogEntry(string id, string title, string category, string description, IEnumerable<string> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(category); ArgumentNullException.ThrowIfNull(description); ArgumentNullException.ThrowIfNull(tags);
        Id = id; Title = title; Category = category; Description = description;
        var owned = tags.Select(t => { ArgumentException.ThrowIfNullOrWhiteSpace(t); return t.Trim(); }).Distinct(StringComparer.OrdinalIgnoreCase).Take(65).ToArray();
        if (owned.Length > 64) throw new ArgumentException("At most 64 tags are supported per entry.", nameof(tags));
        Tags = Array.AsReadOnly(owned); TagsText = string.Join(" · ", owned);
        SearchText = string.Join('\n', id, title, category, description, TagsText);
    }
}

/// <summary>Keyword/phrase AND search, category filtering, AND/OR tag filters, and bounded
/// portable favorites. Search metadata is retained; repeated identical filters return the same
/// result list. Favorites imports validate the entire file before changing any state.</summary>
public sealed class CatalogBrowserModel
{
    public const int MaximumFavoritesFileLength = 65536;
    private readonly Dictionary<string, CatalogEntry> _byId;
    private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
    private string? _filterKey;
    private IReadOnlyList<CatalogEntry> _filtered = Array.Empty<CatalogEntry>();
    public IReadOnlyList<CatalogEntry> Entries { get; }
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<string> Tags { get; }
    public long FilterPasses { get; private set; }
    public int FavoriteCount => _favorites.Count;
    public event EventHandler? FavoritesChanged;
    public CatalogBrowserModel(IEnumerable<CatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var owned = entries.Take(100001).ToArray();
        if (owned.Length > 100000 || owned.Any(e => e == null)) throw new ArgumentException("A catalog supports at most 100000 non-null entries.", nameof(entries));
        _byId = owned.ToDictionary(e => e.Id, StringComparer.Ordinal);
        Entries = Array.AsReadOnly(owned);
        Categories = Array.AsReadOnly(owned.Select(e => e.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Tags = Array.AsReadOnly(owned.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public CatalogEntry Find(string id) => _byId.TryGetValue(id, out var entry) ? entry : throw new ArgumentException("Unknown catalog entry: " + id, nameof(id));
    public bool IsFavorite(string id) => _favorites.Contains(id);
    public void SetFavorite(string id, bool value)
    {
        Find(id);
        var changed = value ? _favorites.Add(id) : _favorites.Remove(id);
        if (changed) { _filterKey = null; FavoritesChanged?.Invoke(this, EventArgs.Empty); }
    }
    public IReadOnlyList<CatalogEntry> Filter(string query = "", string? category = null,
        IEnumerable<string>? tags = null, bool matchAllTags = true, bool favoritesOnly = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > 512) throw new ArgumentException("Search text is limited to 512 characters.", nameof(query));
        var words = Tokenize(query);
        var selected = tags?.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(65).ToArray() ?? [];
        if (selected.Length > 64 || selected.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid tag selection.", nameof(tags));
        // Length-prefixed fields prevent separator characters inside a phrase/tag creating key collisions.
        var key = new StringBuilder();
        void Field(string value) => key.Append(value.Length).Append(':').Append(value.ToUpperInvariant());
        foreach (var word in words) Field(word);
        key.Append(category == null ? "|N" : "|V"); Field(category ?? ""); key.Append('|'); foreach (var tag in selected) Field(tag);
        key.Append(matchAllTags ? 'A' : 'O').Append(favoritesOnly ? 'F' : '*');
        var normalized = key.ToString();
        if (_filterKey == normalized) return _filtered;
        bool Matches(CatalogEntry entry) => (category == null || string.Equals(category, entry.Category, StringComparison.OrdinalIgnoreCase)) &&
            (!favoritesOnly || _favorites.Contains(entry.Id)) && words.All(word => entry.SearchText.Contains(word, StringComparison.OrdinalIgnoreCase)) &&
            (selected.Length == 0 || (matchAllTags ? selected.All(t => entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) : selected.Any(t => entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))));
        var result = Array.AsReadOnly(Entries.Where(Matches).ToArray());
        _filtered = result; _filterKey = normalized; FilterPasses++; return result;
    }
    private static string[] Tokenize(string text)
    {
        List<string> terms = []; var term = new StringBuilder(); var quoted = false;
        void Flush() { if (term.Length != 0) { terms.Add(term.ToString()); term.Clear(); } }
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted) Flush(); else term.Append(c);
        }
        // An unfinished quote is an in-progress phrase, not an exception on every keystroke.
        Flush(); return terms.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string ExportFavorites()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("format", "FastChartsScene.Favorites"); writer.WriteNumber("version", 1);
            writer.WriteStartArray("ids"); foreach (var id in _favorites.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        var text = Encoding.UTF8.GetString(stream.ToArray());
        if (text.Length > MaximumFavoritesFileLength) throw new InvalidOperationException("Favorites exceed the portable file limit.");
        return text;
    }
    public void ImportFavorites(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaximumFavoritesFileLength) throw new FormatException("Favorites file exceeds 64 KiB of text.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a favorites object.");
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 3 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 3 ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "FastChartsScene.Favorites" ||
            !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1 ||
            !root.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > Entries.Count)
            throw new FormatException("Unsupported or malformed favorites file.");
        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ids.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } id || !_byId.ContainsKey(id) || !next.Add(id))
                throw new FormatException("Favorite IDs must be known and unique.");
        }
        if (_favorites.SetEquals(next)) return;
        _favorites.Clear(); _favorites.UnionWith(next); _filterKey = null; FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }
}
