using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static void RegisterCursorIndexReviewTests()
    {
        Add("freeform cursor cold allocation is bounded by exact retained segment and node arrays", () => {
            // Warm runtime/type infrastructure on a different source, not this index.
            using (var warm = InterpolatedDocument(new FreeformCursorData(new XyData([1, 2], [3, 4]))))
                InterpolatedValues(warm, 1.5, 10);
            const int count = 50000;
            var raw = new UniformData(0, 10.0 / (count - 1), Enumerable.Repeat(8.0, count).ToArray());
            using var d = InterpolatedDocument(new FreeformCursorData(raw));
            var frame = new SceneCompiler().Compile(d, 800, 600); var input = new ChartInteraction(d);
            List<SeriesCursorValue> rows = new(1); var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(4.321, 19));
            var before = GC.GetAllocatedBytesForCurrentThread();
            input.QuerySeriesCursorValues(frame, point, rows, SeriesCursorTrackingMode.Interpolated);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Equal(1, rows.Count); Equal(8, rows[0].ResultData.Y); Equal(1, input.CursorInterpolationIndexBuildCount);
            // 49,999 x 40-byte segments + 8,191 x 48-byte nodes, plus modest
            // object/cache overhead. Previous growable-list construction used >8 MB.
            True(allocated < 3_000_000, $"Cold cursor allocated {allocated} bytes.");
            before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) input.QuerySeriesCursorValues(frame, point, rows, SeriesCursorTrackingMode.Interpolated);
            Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Equal(1, input.CursorInterpolationIndexBuildCount);
        });
        Add("all-gap freeform cursor retains no sample-sized arrays and sparse isolated knots survive", () => {
            const int count = 50000;
            var samples = Enumerable.Repeat(double.NaN, count).ToArray();
            using var d = InterpolatedDocument(new FreeformCursorData(new UniformData(0, .0001, samples)));
            var compiler = new SceneCompiler(); var frame = compiler.Compile(d, 800, 600); var input = new ChartInteraction(d);
            var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(2.5, 19)); List<SeriesCursorValue> rows = new(1);
            var before = GC.GetAllocatedBytesForCurrentThread();
            input.QuerySeriesCursorValues(frame, point, rows, SeriesCursorTrackingMode.Interpolated);
            True(GC.GetAllocatedBytesForCurrentThread() - before < 8192); Equal(0, rows.Count);
            samples[25000] = 7;
            ((LineSeries)d.Series[0]).Data = new FreeformCursorData(new UniformData(0, .0001, samples));
            frame = compiler.Compile(d, 800, 600);
            input.QuerySeriesCursorValues(frame, point, rows, SeriesCursorTrackingMode.Interpolated);
            Equal(1, rows.Count); Equal(25000, rows[0].Index); Equal(7, rows[0].Data.Y); True(!rows[0].IsInterpolated);
        });
        Add("exact cursor tree sizing preserves results around leaf split boundaries and gaps", () => {
            foreach (var count in new[] { 2, 16, 17, 32, 33, 128, 129, 257 })
            foreach (var method in new[] { LineInterpolation.Linear, LineInterpolation.StepBefore, LineInterpolation.StepAfter })
            {
                var data = new UniformData(0, 10.0 / (count - 1),
                    Enumerable.Range(0, count).Select(i => i % 7 == 0 ? double.NaN : 10 + Math.Sin(i)).ToArray());
                using var d = InterpolatedDocument(data, method); var line = (LineSeries)d.Series[0];
                var compiler = new SceneCompiler(); var input = new ChartInteraction(d); List<SeriesCursorValue> rows = [];
                var frame = compiler.Compile(d, 800, 600);
                var points = new[] { new PointD(1, 19), new PointD(4.321, 11), new PointD(8.5, 2) };
                var expected = new SeriesCursorValue[points.Length][];
                for (var i = 0; i < points.Length; i++)
                {
                    input.QuerySeriesCursorValues(frame, frame.Layout.ToScreen(d.XAxis, d.YAxis, points[i]), rows, SeriesCursorTrackingMode.Interpolated);
                    expected[i] = rows.ToArray();
                }
                line.Data = new FreeformCursorData(data); frame = compiler.Compile(d, 800, 600);
                for (var i = 0; i < points.Length; i++)
                {
                    input.QuerySeriesCursorValues(frame, frame.Layout.ToScreen(d.XAxis, d.YAxis, points[i]), rows, SeriesCursorTrackingMode.Interpolated);
                    True(expected[i].SequenceEqual(rows), $"Changed bounds at {count}/{method}/{i}.");
                }
            }
        });
    }
}
