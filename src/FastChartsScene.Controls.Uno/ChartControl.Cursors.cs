using FastChartsScene.Interaction;

namespace FastChartsScene.Controls.Uno;

public sealed partial class ChartControl
{
    /// <summary>Current nearest original sample. Cleared outside the plot, over legends,
    /// on document replacement/unload, and when no visible source sample is in range.</summary>
    public DataHit? DataHit => _overlay.Hit;
    public event EventHandler? DataHitChanged;
    private void SetDataHit(DataHit? hit)
    {
        if (Equals(_overlay.Hit, hit)) return;
        _overlay.Hit = hit; DataHitChanged?.Invoke(this, EventArgs.Empty);
    }
}
