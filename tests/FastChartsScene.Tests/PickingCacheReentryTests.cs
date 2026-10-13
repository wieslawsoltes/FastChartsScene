using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Interaction;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    private sealed class ReentrantPickingData(IDataSource source) : IDataSource
    {
        public Action? Once;
        public int Count => source.Count;
        public bool IsOrdered => false;
        public double X(int index) { var callback = Once; Once = null; callback?.Invoke(); return source.X(index); }
        public double Y(int index) => source.Y(index);
    }
    private static void RegisterPickingCacheReentryTests()
    {
        Add("cold reentrant point nearest-X and segment indexes retain one canonical source entry", () => {
            for (var kind = 0; kind < 3; kind++)
            {
                var data = new ReentrantPickingData(new UniformData(0, .002, Enumerable.Range(0, 4096).Select(i => 10 + Math.Sin(i * .01)).ToArray()));
                using var d = InterpolatedDocument(data); var frame = new SceneCompiler().Compile(d, 800, 600);
                var input = new ChartInteraction(d); var point = frame.Layout.ToScreen(d.XAxis, d.YAxis, new(5.01, 10.03));
                List<SeriesCursorValue> outer = [], inner = []; DataHit? expectedHit = null; var called = false;
                var mode = kind == 1 ? SeriesCursorTrackingMode.Interpolated : SeriesCursorTrackingMode.NearestX;
                data.Once = () => {
                    called = true;
                    if (kind == 2) expectedHit = input.HitTestData(frame, point, 1000);
                    else input.QuerySeriesCursorValues(frame, point, inner, mode);
                };
                void Query()
                {
                    if (kind == 2)
                    {
                        var actualHit = input.HitTestData(frame, point, 1000);
                        True(actualHit != null && expectedHit != null && actualHit == expectedHit);
                    }
                    else
                    {
                        input.QuerySeriesCursorValues(frame, point, outer, mode);
                        Equal(1, outer.Count); True(outer.SequenceEqual(inner));
                    }
                }
                Query(); True(called && data.Once == null);
                long Builds() => kind == 1 ? input.CursorInterpolationIndexBuildCount : input.SampleIndexBuildCount;
                // Two constructors actually ran; only one canonical entry remains.
                // Subsequent queries must reuse it, without a duplicate Add exception.
                Equal(2, Builds()); Query(); Equal(2, Builds());
                input.ClearPickingCache(); Query(); Equal(3, Builds());
            }
        });
    }
}
