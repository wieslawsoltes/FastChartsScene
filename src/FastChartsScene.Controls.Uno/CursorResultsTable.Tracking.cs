using FastChartsScene.Interaction;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastChartsScene.Controls.Uno;

public sealed partial class CursorResultsTable
{
    public static readonly DependencyProperty TrackingModeProperty = DependencyProperty.Register(
        nameof(TrackingMode), typeof(SeriesCursorTrackingMode), typeof(CursorResultsTable),
        new PropertyMetadata(SeriesCursorTrackingMode.NearestX, TrackingModePropertyChanged));
    public static readonly DependencyProperty TrackingModeSelectionEnabledProperty = DependencyProperty.Register(
        nameof(TrackingModeSelectionEnabled), typeof(bool), typeof(CursorResultsTable),
        new PropertyMetadata(false, (d, _) => ((CursorResultsTable)d).SyncTrackingMode()));
    /// <summary>Requested query mode. Changing it clears snapshots/Hold and raises
    /// TrackingModeChanged; the host supplies fresh values for this mode.</summary>
    public SeriesCursorTrackingMode TrackingMode
    {
        get => (SeriesCursorTrackingMode)GetValue(TrackingModeProperty);
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            SetValue(TrackingModeProperty, value);
        }
    }
    /// <summary>Opt-in compact native selector. The table does not own or query a chart.</summary>
    public bool TrackingModeSelectionEnabled
    {
        get => (bool)GetValue(TrackingModeSelectionEnabledProperty);
        set => SetValue(TrackingModeSelectionEnabledProperty, value);
    }
    public event EventHandler? TrackingModeChanged;
    private bool _trackingReady, _syncingTrackingMode, _restoringTrackingMode;
    private static void TrackingModePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var table = (CursorResultsTable)sender;
        if (!Enum.IsDefined((SeriesCursorTrackingMode)e.NewValue))
        {
            table._restoringTrackingMode = true;
            try { table.SetValue(TrackingModeProperty, e.OldValue); }
            finally { table._restoringTrackingMode = false; }
            throw new ArgumentOutOfRangeException(nameof(TrackingMode));
        }
        if (!table._trackingReady || table._restoringTrackingMode) return;
        table.SyncTrackingMode(); table.Clear();
        table.TrackingModeChanged?.Invoke(table, EventArgs.Empty);
    }
    private void SyncTrackingMode()
    {
        if (!_trackingReady) return;
        _syncingTrackingMode = true;
        try
        {
            TrackingModeChoice.Visibility = TrackingModeSelectionEnabled ? Visibility.Visible : Visibility.Collapsed;
            TrackingModeChoice.SelectedIndex = (int)TrackingMode;
        }
        finally { _syncingTrackingMode = false; }
    }
    private void TrackingModeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_trackingReady || _syncingTrackingMode || TrackingModeChoice.SelectedIndex < 0) return;
        TrackingMode = (SeriesCursorTrackingMode)TrackingModeChoice.SelectedIndex;
    }
    private void UpdateResultHeaders(bool calculated = false)
    {
        if (ResultXHeader == null) return;
        var mixed = calculated || TrackingMode == SeriesCursorTrackingMode.Interpolated;
        ResultXHeader.Text = mixed ? "Result X" : "Original X";
        ResultValueHeader.Text = mixed ? "Result value" : "Original value";
    }
}
