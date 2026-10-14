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
    /// <summary>Opt-in all-series sampling in the selected tracking mode, independent of single-hit
    /// cursors. Disabled by default to avoid querying series that no host displays.</summary>
    public bool CursorValuesEnabled
    {
        get => (bool)GetValue(CursorValuesEnabledProperty);
        set { ObjectDisposedException.ThrowIf(_disposed, this); SetValue(CursorValuesEnabledProperty, value); }
    }
    public static readonly DependencyProperty CursorValuesTrackingModeProperty = DependencyProperty.Register(
        nameof(CursorValuesTrackingMode), typeof(SeriesCursorTrackingMode), typeof(ChartControl),
        new PropertyMetadata(SeriesCursorTrackingMode.NearestX, CursorValuesTrackingModeChanged));
    /// <summary>Session-only table query mode. NearestX remains the default. Interpolated
    /// line/step results retain their original endpoints separately from ResultData.</summary>
    public SeriesCursorTrackingMode CursorValuesTrackingMode
    {
        get => (SeriesCursorTrackingMode)GetValue(CursorValuesTrackingModeProperty);
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            SetValue(CursorValuesTrackingModeProperty, value);
        }
    }
    private static void CursorValuesTrackingModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (ChartControl)sender;
        if (!Enum.IsDefined((SeriesCursorTrackingMode)e.NewValue))
        {
            control._restoringCursorMode = true;
            try { control.SetValue(CursorValuesTrackingModeProperty, e.OldValue); }
            finally { control._restoringCursorMode = false; }
            throw new ArgumentOutOfRangeException(nameof(CursorValuesTrackingMode));
        }
        if (!control._restoringCursorMode) control.RefreshSeriesCursorValues();
    }
    private readonly List<SeriesCursorValue> _cursorValues = [], _cursorValueDraft = [];
    private bool _restoringCursorMode;
    private long _cursorValuesGeneration;
    private int _cursorValuesQueryDepth;
    private ReadOnlyCollection<SeriesCursorValue>? _cursorValueView;
    /// <summary>Read-only live view. Copy values (ToArray) for a retained snapshot.
    /// Entries retain original samples/endpoints and optional calculated values, never
    /// mutable source references.</summary>
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
        // A source getter can reenter through host callbacks. Give a nested query its
        // own scratch list and reject older generations without erasing the newer
        // publication. Ordinary non-reentrant pointer queries retain their one list.
        if (_cursorValuesQueryDepth >= 8) { ClearSeriesCursorValues(); return; }
        var generation = ++_cursorValuesGeneration;
        var draft = _cursorValuesQueryDepth == 0 ? _cursorValueDraft : new List<SeriesCursorValue>();
        draft.Clear();
        var owner = Document; var input = _input; var mode = CursorValuesTrackingMode;
        var active = CursorValuesEnabled && owner is { IsDisposed: false } && input != null &&
            frame.Metadata.Revision == owner.Revision && owner.View == ChartView.XY && frame.Layout.Plot.Contains(point);
        if (active)
        {
            var revision = owner!.Revision;
            _cursorValuesQueryDepth++;
            try { input!.QuerySeriesCursorValues(frame, point, draft, mode); CursorValuesQueryCount++; }
            finally { _cursorValuesQueryDepth--; }
            if (generation != _cursorValuesGeneration) return;
            if (!ReferenceEquals(owner, Document) || !ReferenceEquals(input, _input) || owner.IsDisposed || owner.Revision != revision ||
                !CursorValuesEnabled || mode != CursorValuesTrackingMode)
            { if (ReferenceEquals(owner, Document)) ClearSeriesCursorValues(); return; }
        }
        PublishSeriesCursorValues(owner, active, draft);
    }
    private void ClearSeriesCursorValues()
    {
        _cursorValuesGeneration++;
        if (_cursorValuesQueryDepth == 0) _cursorValueDraft.Clear();
        PublishSeriesCursorValues(Document, false, Array.Empty<SeriesCursorValue>());
    }
    private void PublishSeriesCursorValues(ChartDocument? owner, bool active, IReadOnlyList<SeriesCursorValue> draft)
    {
        var changed = !ReferenceEquals(owner, CursorValuesDocument) || active != CursorValuesAreActive || _cursorValues.Count != draft.Count;
        if (!changed)
            for (var i = 0; i < _cursorValues.Count; i++)
                if (_cursorValues[i] != draft[i]) { changed = true; break; }
        if (!changed) return;
        _cursorValues.Clear();
        for (var i = 0; i < draft.Count; i++) _cursorValues.Add(draft[i]);
        CursorValuesDocument = owner; CursorValuesAreActive = active; CursorValuesPublishCount++;
        SeriesCursorValuesChanged?.Invoke(this, EventArgs.Empty);
    }
}
