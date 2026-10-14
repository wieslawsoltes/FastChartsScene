using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static void RegisterCursorPlateauTests()
    {
        Add("horizontal cursor plateaus retain original binary64 bits across fractions axes and source indexes", () => {
            foreach (var method in new[] { LineInterpolation.Linear, LineInterpolation.StepBefore, LineInterpolation.StepAfter })
            foreach (var logarithmic in new[] { false, true })
            foreach (var inverted in new[] { false, true })
            foreach (var freeform in new[] { false, true })
            {
                var plateau = Math.E;
                IDataSource data = method switch {
                    LineInterpolation.StepBefore => new XyData([2, 8], [Math.PI, plateau]),
                    LineInterpolation.StepAfter => new XyData([2, 8], [plateau, Math.PI]),
                    _ => new XyData([2, 8], [plateau, plateau])
                };
                if (freeform) data = new FreeformCursorData(data);
                using var d = InterpolatedDocument(data, method);
                d.YAxis.Range = new(1, 100);
                d.YAxis.Scale = logarithmic ? AxisScale.Logarithmic : AxisScale.Linear;
                d.YAxis.Inverted = inverted; d.XAxis.Inverted = inverted;
                var frame = new SceneCompiler().Compile(d, 800, 600);
                var input = new ChartInteraction(d); List<SeriesCursorValue> values = [];
                for (var i = 1; i < 1000; i++)
                {
                    var location = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2 + 6.0 * i / 1000, 19));
                    input.QuerySeriesCursorValues(frame, location, values, SeriesCursorTrackingMode.Interpolated);
                    Equal(1, values.Count); True(values[0].IsInterpolated);
                    True(BitConverter.DoubleToInt64Bits(values[0].ResultData.Y) == BitConverter.DoubleToInt64Bits(plateau),
                        $"Plateau lost original bits: {method}/{logarithmic}/{inverted}/{freeform}/{i}");
                    Equal(0, values[0].Index); Equal(1, values[0].Interpolation!.Value.EndIndex);
                    True(values[0].Data == new PointD(data.X(0), data.Y(0)));
                    True(values[0].Interpolation!.Value.EndData == new PointD(data.X(1), data.Y(1)));
                }
                Equal(freeform ? 1 : 0, input.CursorInterpolationIndexBuildCount);
            }
        });
    }
}
