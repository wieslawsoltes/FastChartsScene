using System.Collections.ObjectModel;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;
using Microsoft.UI.Xaml;

namespace FastChartsScene.Controls.Uno;

public sealed partial class ChartControl
{
    public static readonly DependencyProperty CursorValuesEnabledProperty = DependencyProperty.Register(
        nameof(CursorValuesEnabled), typeof(bool), typeof(ChartControl), new PropertyMetadata(false,
            (d, _) => ((ChartControl)d).RefreshSeriesCursorValues()));
    /// <summary>Opt-in all-series original nearest-X sampling, independent of single-hit
    /// cursors. Disabled by default to avoid querying series that no host displays.</summary>
    public bool CursorValuesEnabled
    {
        get => (bool)GetValue(CursorValuesEnabledProperty);
        set { ObjectDisposedException.ThrowIf(_disposed, this); SetValue(CursorValuesEnabledProperty, value); }
    }
    private readonly List<SeriesCursorValue> _cursorValues = [], _cursorValueDraft = [];
    private ReadOnlyCollection<SeriesCursorValue>? _cursorValueView;
    /// <summary>Read-only live view. Copy values (ToArray) for a retained snapshot.
    /// Entries contain original values, not mutable source references.</summary>
    public IReadOnlyList<SeriesCursorValue> SeriesCursorValues => _cursorValueView ??= _cursorValues.AsReadOnly();
    public ChartDocument? CursorValuesDocument { get; private set; }
    public bool CursorValuesAreActive { get; private set; }
    public event EventHandler? SeriesCursorValuesChanged;
    public long CursorValuesQueryCount { get; private set; }
    public long CursorValuesPublishCount { get; private set; }

    private void RefreshSeriesCursorValues()
    {
        if (_disposed) return;
        if (Frame is { } frame && _overlay.Cursor is { } point) UpdateSeriesCursorValues(frame, point);
        else ClearSeriesCursorValues();
    }
    private void UpdateSeriesCursorValues(RenderFrame frame, PointD point)
    {
        _cursorValueDraft.Clear();
        var owner = Document; var input = _input;
        var active = CursorValuesEnabled && owner is { IsDisposed: false } && input != null &&
            frame.Metadata.Revision == owner.Revision && owner.View == ChartView.XY && frame.Layout.Plot.Contains(point);
        if (active)
        {
            var revision = owner!.Revision;
            input!.QuerySeriesCursorValues(frame, point, _cursorValueDraft); CursorValuesQueryCount++;
            // Custom IDataSource implementations must be immutable, but cannot be allowed
            // to publish values into a different owner through reentrant host callbacks.
            if (!ReferenceEquals(owner, Document) || !ReferenceEquals(input, _input) || owner.IsDisposed || owner.Revision != revision)
            { if (ReferenceEquals(owner, Document)) ClearSeriesCursorValues(); return; }
        }
        PublishSeriesCursorValues(owner, active);
    }
    private void ClearSeriesCursorValues()
    { _cursorValueDraft.Clear(); PublishSeriesCursorValues(Document, false); }
    private void PublishSeriesCursorValues(ChartDocument? owner, bool active)
    {
        var changed = !ReferenceEquals(owner, CursorValuesDocument) || active != CursorValuesAreActive || _cursorValues.Count != _cursorValueDraft.Count;
        if (!changed)
            for (var i = 0; i < _cursorValues.Count; i++)
                if (_cursorValues[i] != _cursorValueDraft[i]) { changed = true; break; }
        if (!changed) return;
        _cursorValues.Clear(); _cursorValues.AddRange(_cursorValueDraft);
        CursorValuesDocument = owner; CursorValuesAreActive = active; CursorValuesPublishCount++;
        SeriesCursorValuesChanged?.Invoke(this, EventArgs.Empty);
    }
}
