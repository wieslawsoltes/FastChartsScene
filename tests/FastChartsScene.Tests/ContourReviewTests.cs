using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Rendering;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterContourReviewTests()
    {
        Add("contours do not allocate per cell or level for empty results", () => {
            var grid=new ScalarGrid(256,256,0,1,0,1,new double[256*256]); double[] levels=[1,2,3];
            GeometryAlgorithms.Contours(grid,levels);
            var before=GC.GetAllocatedBytesForCurrentThread(); var result=GeometryAlgorithms.Contours(grid,levels);
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Equal(0,result.Count); True(allocated<4096,$"Empty contour allocated {allocated} bytes.");
        });
        Add("contour topology cache survives pan palette and resize but not grid or level changes", () => {
            using var d=new ChartDocument(); var s=new ContourSeries { Grid=new(2,2,0,1,0,1,[0,1,1,0]) }; s.SetLevels([.5]); d.Series.Add(s);
            var c=new SceneCompiler(); c.Compile(d,800,600); Equal(1,c.ContourGenerationCount);
            d.XAxis.Range=new(-1,2); s.ColorScale=s.ColorScale.WithPalette(ColorMap.Thermal); c.Compile(d,1000,700); Equal(1,c.ContourGenerationCount);
            s.SetLevels([.25,.75]); c.Compile(d,1000,700); Equal(2,c.ContourGenerationCount);
            s.Grid=new(2,2,0,1,0,1,[1,0,0,1]); c.Compile(d,1000,700); Equal(3,c.ContourGenerationCount);
            c.ClearCache(); c.Compile(d,1000,700); Equal(4,c.ContourGenerationCount);
        });
        Add("contours handle finite extreme crossings and saddle determinants", () => {
            var m=double.MaxValue; var grid=new ScalarGrid(2,2,0,1,0,1,[-m,m,m,-m]);
            var lines=GeometryAlgorithms.Contours(grid,[0]); Equal(2,lines.Count);
            True(lines.All(l=>l.A.IsFinite&&l.B.IsFinite));
            foreach(var line in lines) True((line.A.X==.5||line.A.Y==.5)&&(line.B.X==.5||line.B.Y==.5));
            Throws<ArgumentOutOfRangeException>(()=>GeometryAlgorithms.Contours(grid,[double.NaN]));
        });
        Add("allocation-free contour cells preserve the previous finite marching-squares output", () => {
            var random=new Random(2957);
            for(var trial=0;trial<20;trial++)
            {
                var values=Enumerable.Range(0,9*11).Select(i=>i%19==0?double.NaN:random.NextDouble()*4-2).ToArray();
                var grid=new ScalarGrid(9,11,-2,3,-4,5,values); double[] levels=[-1,-.25,0,.75,1.5];
                var actual=GeometryAlgorithms.Contours(grid,levels); var expected=ReferenceContours(grid,levels);
                True(actual.SequenceEqual(expected),"Contour segment ordering or coordinates changed.");
            }
        });
    }
    // Frozen finite-input reference to detect changes in edge ordering and saddle selection.
    private static IReadOnlyList<ContourSegment> ReferenceContours(ScalarGrid grid,double[] levels)
    {
        List<ContourSegment> segments=[];
        foreach(var level in levels) for(var row=0;row<grid.Rows-1;row++) for(var col=0;col<grid.Columns-1;col++)
        {
            double[] v=[grid[col,row],grid[col+1,row],grid[col+1,row+1],grid[col,row+1]];
            if(v.Any(double.IsNaN))continue;
            PointD[] p=[new(grid.X(col),grid.Y(row)),new(grid.X(col+1),grid.Y(row)),new(grid.X(col+1),grid.Y(row+1)),new(grid.X(col),grid.Y(row+1))];
            List<PointD> hits=[];
            for(var e=0;e<4;e++)
            {
                var j=(e+1)%4;if((v[e]>=level)==(v[j]>=level))continue;
                var t=(level-v[e])/(v[j]-v[e]);hits.Add(new(p[e].X+t*(p[j].X-p[e].X),p[e].Y+t*(p[j].Y-p[e].Y)));
            }
            if(hits.Count==2)segments.Add(new(hits[0],hits[1],level));
            else if(hits.Count==4)
            {
                var determinant=(v[0]-level)*(v[2]-level)-(v[1]-level)*(v[3]-level);
                if(determinant>=0){segments.Add(new(hits[0],hits[1],level));segments.Add(new(hits[2],hits[3],level));}
                else{segments.Add(new(hits[0],hits[3],level));segments.Add(new(hits[1],hits[2],level));}
            }
        }
        return segments;
    }
}
