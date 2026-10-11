using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterContourReviewTests();
    static partial void RegisterPerformanceReviewTests()
    {
        RegisterContourReviewTests();
        Add("viewport heatmap projection equals exhaustive cells under inversion and log clipping", () => {
            var random = new Random(640);
            foreach (var dimensions in new[] { (1,1), (1,17), (19,1), (19,17) })
            for (var iteration = 0; iteration < 24; iteration++)
            {
                using var d = new ChartDocument();
                var g = new ScalarGrid(dimensions.Item1, dimensions.Item2, -1, 8, -2, 12, new double[dimensions.Item1 * dimensions.Item2]);
                d.XAxis.Range = new(.05 + random.NextDouble() * 4, 6 + random.NextDouble() * 4);
                d.YAxis.Range = new(.05 + random.NextDouble() * 4, 7 + random.NextDouble() * 8);
                d.XAxis.Inverted = (iteration & 1) != 0; d.YAxis.Inverted = (iteration & 2) != 0;
                if ((iteration & 4) != 0) d.XAxis.Scale = AxisScale.Logarithmic;
                if ((iteration & 8) != 0) d.YAxis.Scale = AxisScale.Logarithmic;
                var l = ChartLayout.Create(d, 960, 600); var view = IntensityViewport.Create(l, d.XAxis, d.YAxis, g);
                var expected = new Dictionary<(int,int), RectD>();
                for (var row = 0; row < g.Rows; row++) for (var col = 0; col < g.Columns; col++)
                    if (IntensityGeometry.TryCellBounds(l, d.XAxis, d.YAxis, g, col, row, out var rect)) expected.Add((col,row), rect);
                Equal(expected.Count, view.CellCount);
                foreach (var row in view.Rows.Span) foreach (var col in view.Columns.Span)
                {
                    True(expected.TryGetValue((col.Index,row.Index), out var rect));
                    Equal(rect.X,col.Start); Equal(rect.Y,row.Start); Equal(rect.Width,col.Length); Equal(rect.Height,row.Length);
                }
            }
        });
        Add("zoomed million-cell grid projects only visible cells and preserves exact boundary ties", () => {
            using var d = new ChartDocument { ShowGrid=false, ShowLegend=false };
            var g = new ScalarGrid(1024,1024,0,100,0,100,new double[1024*1024]);
            d.XAxis.Range = new(49.95,50.05); d.YAxis.Range = new(49.95,50.05);
            var l = ChartLayout.Create(d,800,600); var view = IntensityViewport.Create(l,d.XAxis,d.YAxis,g);
            True(view.CellCount>0 && view.CellCount<=16);
            d.Series.Add(new HeatmapSeries { Grid=g }); var f=new SceneCompiler().Compile(d,800,600);
            True(f.Metadata.VertexCount<500);
            var boundary=g.CellBounds(511,511).Right; d.XAxis.Range=new(boundary,100);
            view=IntensityViewport.Create(ChartLayout.Create(d,800,600),d.XAxis,d.YAxis,g);
            True(view.Columns.Span[0].Index==512);
        });
        Add("offscreen grids produce empty projections", () => {
            using var d=new ChartDocument(); var g=new ScalarGrid(8,8,20,30,20,30,new double[64]);
            var v=IntensityViewport.Create(ChartLayout.Create(d,800,600),d.XAxis,d.YAxis,g); Equal(0,v.CellCount);
        });
        Add("grid statistics preserve empty positive and constant domains and share cached identity", () => {
            var g=new ScalarGrid(3,2,0,2,0,1,[-4,0,2,7,double.NaN,2]); var stats=g.Statistics;
            Equal(5,stats.FiniteCount); Equal(-4,stats.Minimum); Equal(7,stats.Maximum);
            Equal(3,stats.PositiveCount); Equal(2,stats.PositiveMinimum); Equal(7,stats.PositiveMaximum);
            var a=new HeatmapSeries { Grid=g }; var b=new HeatmapSeries { Grid=g, ColorScale=new(mode:ColorScaleMode.Logarithmic) };
            Equal(-4,a.ResolveColorScale().Minimum); Equal(2,b.ResolveColorScale().Minimum);
            a.ColorScale=a.ColorScale.WithPalette(ColorMap.Grayscale); a.ResolveColorScale(); True(ReferenceEquals(stats,g.Statistics));
            var empty=new ScalarGrid(1,1,0,1,0,1,[double.NaN]); True(double.IsNaN(empty.Statistics.Minimum));
            True(!new HeatmapSeries { Grid=empty }.ResolveColorScale().HasValues);
        });
        Add("no-op palette settings preserve immutable identities", () => {
            var scale=new ColorScale(ColorMap.Grayscale,new(1,10));
            True(ReferenceEquals(scale,scale.WithPalette(scale.Palette)) && ReferenceEquals(scale,scale.WithRange(scale.Range)));
            True(ReferenceEquals(scale,scale.WithMode(scale.Mode)) && ReferenceEquals(scale,scale.WithReversed(scale.Reversed)));
            True(ReferenceEquals(scale,scale.WithMissingColor(scale.MissingColor)) && ReferenceEquals(scale,scale.WithOutOfRange(scale.OutOfRange)));
            True(ReferenceEquals(scale.Palette,scale.Palette.WithInterpolation(scale.Palette.Interpolation)));
        });
        Add("bulk rolling append matches scalar FIFO including oversized and empty batches", () => {
            var random=new Random(944); var buffer=new RollingBuffer(31,100,.25); var expected=new Queue<double>(); long written=0;
            for(var iteration=0;iteration<200;iteration++)
            {
                var values=Enumerable.Range(0,random.Next(0,100)).Select(i=>i%13==0?double.NaN:random.NextDouble()).ToArray();
                buffer.Append(values); written+=values.Length;
                foreach(var value in values) { expected.Enqueue(value); if(expected.Count>31) expected.Dequeue(); }
                var snapshot=buffer.Snapshot(); Equal(expected.Count,snapshot.Count); Equal(written,buffer.SamplesWritten);
                var i=0; foreach(var value in expected) { True(value.Equals(snapshot.Y(i))); Equal(100+(written-expected.Count+i)*.25,snapshot.X(i)); i++; }
            }
            buffer.Clear(); Equal(0,buffer.Snapshot().Count); Equal(0,buffer.SamplesWritten);
        });
        Add("rolling snapshot allocates one owned sample buffer and survives future writes", () => {
            var buffer=new RollingBuffer(100000); buffer.Append(new double[100000]); buffer.Snapshot();
            var before=GC.GetAllocatedBytesForCurrentThread(); var snapshot=buffer.Snapshot(); var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            True(allocated<900000,$"Snapshot allocated {allocated} bytes; expected one 800000-byte sample array.");
            buffer.Append([19,20]); Equal(0,snapshot.Y(snapshot.Count-1)); Equal(20,buffer.Snapshot().Y(99999));
        });
        Add("failed compilation cannot poison the last-good frame cache", () => {
            using var d=LineDocument(); var c=new SceneCompiler(); c.Compile(d,800,600); var s=d.Series[0]; var color=s.Color;
            s.Color=new(float.NaN,0,0); Throws<InvalidOperationException>(()=>c.Compile(d,800,600));
            Throws<InvalidOperationException>(()=>c.Compile(d,800,600)); Equal(1,c.CompilationCount);
            s.Color=color; c.Compile(d,800,600).Validate(); Equal(2,c.CompilationCount);
        });
        Add("disposed documents are rejected even when a frame is cached", () => {
            var d=LineDocument(); var c=new SceneCompiler(); c.Compile(d,800,600); d.Dispose();
            Throws<ObjectDisposedException>(()=>c.Compile(d,800,600));
        });
    }
}
