using FastChartsScene.Controls.Uno;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;

namespace PackageConsumer;

public static class SampleCursorConsumer
{
    public static PointQueryResult Query(IDataSource data, Axis x, Axis y, RectD plot, PointD cursor) =>
        new PointSpatialIndex(data).Nearest(x, y, plot, cursor);
    public static void Observe(ChartControl chart, Action<DataHit?> onChanged) =>
        chart.DataHitChanged += (_, _) => onChanged(chart.DataHit);
}
