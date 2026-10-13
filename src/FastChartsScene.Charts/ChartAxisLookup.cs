using FastChartsScene.Core;

namespace FastChartsScene.Charts;

public sealed partial class ChartDocument
{
    // Query hot paths must not allocate capturing predicates or boxed enumerators.
    // Read the live collections so replacement/reordering remains immediately visible.
    public Axis FindX(string id)
    {
        for (var i = 0; i < XAxes.Count; i++) if (XAxes[i].Id == id) return XAxes[i];
        throw new InvalidOperationException($"Unknown X axis '{id}'.");
    }
    public Axis FindY(string id)
    {
        for (var i = 0; i < YAxes.Count; i++) if (YAxes[i].Id == id) return YAxes[i];
        throw new InvalidOperationException($"Unknown Y axis '{id}'.");
    }
}
