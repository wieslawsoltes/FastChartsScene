using System.Runtime.CompilerServices;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Rendering;

/// <summary>Compiles chart models into clipped GPU triangles. UI-thread-affine; immutable data indexes
/// are weakly cached, and an unchanged document/size reuses its entire frame without allocating.</summary>
public sealed partial class SceneCompiler
{
    private readonly ConditionalWeakTable<IDataSource, EnvelopeIndex> _indexes = new();
    private ChartDocument? _document;
    private RenderFrame? _frame;
    public long CompilationCount { get; private set; }
    public RenderFrame Compile(ChartDocument document, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(document.IsDisposed, document);
        if (ReferenceEquals(document, _document) && _frame is { } cached && cached.Metadata.Revision == document.Revision &&
            cached.Metadata.Width == width && cached.Metadata.Height == height) return cached;
        var layout = ChartLayout.Create(document, width, height);
        var f = new FrameBuilder(width, height);
        var full = new RectD(0, 0, width, height);
        f.Text(document.Title, width / 2, 28, document.Theme.Text, 20, "center", bold: true);
        if (document.Subtitle.Length > 0) f.Text(document.Subtitle, width / 2, 48, document.Theme.Text.WithAlpha(.7f), 12, "center");
        switch (document.View)
        {
            case ChartView.XY: BuildXY(document, layout, f); break;
            case ChartView.Polar: case ChartView.Smith: BuildCircular(document, layout, f); break;
            case ChartView.ThreeD: case ChartView.Pie3D: ThreeDScene.Build(document, layout, f); break;
            default: throw new NotSupportedException($"Unknown chart view {document.View}.");
        }
        BuildColorBars(document, layout, f);
        if (document.ShowLegend) BuildLegend(document, layout, f);
        var frame = new RenderFrame(f.Vertices.ToArray(), new(width, height, document.Theme.Background.ToHex(),
            f.Batches.ToArray(), f.Labels.ToArray(), f.Hits.ToArray(), layout.Plot, document.Revision,
            document.TotalPoints, f.Count, document.Title), layout);
        frame.Validate(); _frame = frame; _document = document; CompilationCount++;
        return frame;
    }
    public void ClearCache() { _document = null; _frame = null; _indexes.Clear(); }

    private void BuildXY(ChartDocument d, ChartLayout l, FrameBuilder f)
    {
        var full = new RectD(0, 0, f.Width, f.Height); var start = f.Count;
        foreach (var p in l.AxisPlots.Values.Distinct()) f.Rect(p, d.Theme.Plot);
        f.Batch(start, full); BuildAxes(d, l, f);
        var stacks = new Dictionary<(string X, string Y, string Group, bool Horizontal, double Category, bool Positive), double>();
        foreach (var s in d.Series.Where(s => s.Visible))
        {
            var x = d.FindX(s.XAxisId); var y = d.FindY(s.YAxisId); var p = l.ForAxis(s.YAxisId);
            PointD Map(PointD v) => l.ToScreen(x, y, v);
            start = f.Count;
            switch (s)
            {
                case LineSeries line:
                    DrawLine(f, line, x, p, Map); break;
                case BandSeries band:
                    for (var i = 1; i < band.Data.Count; i++)
                    {
                        var a=Map(new(band.Data.X(i-1),band.Data.Y(i-1))); var b=Map(new(band.Data.X(i),band.Data.Y(i)));
                        var c=Map(new(band.Upper.X(i),band.Upper.Y(i))); var e=Map(new(band.Upper.X(i-1),band.Upper.Y(i-1)));
                        FillBand(f,a,b,c,e,band.Color.WithAlpha(.32f),p);
                        f.Line(a,b,band.Color,band.StrokeWidth,p); f.Line(e,c,band.Color,band.StrokeWidth,p);
                    }
                    break;
                case BarSeries bar:
                    for (var i=0;i<bar.Data.Count;i++)
                    {
                        var category=bar.Data.X(i); var value=bar.Data.Y(i); if (!double.IsFinite(value)) continue;
                        var baseline=bar.Baseline;
                        if (bar.StackGroup.Length>0)
                        {
                            var key=(bar.XAxisId,bar.YAxisId,bar.StackGroup,bar.Horizontal,category,value>=0);
                            baseline=stacks.GetValueOrDefault(key,bar.Baseline); stacks[key]=baseline+value; value+=baseline;
                        }
                        PointD a,b;
                        if (bar.Horizontal) { a=Map(new(baseline,category-bar.BarWidth/2)); b=Map(new(value,category+bar.BarWidth/2)); }
                        else { a=Map(new(category-bar.BarWidth/2,baseline)); b=Map(new(category+bar.BarWidth/2,value)); }
                        if (!a.IsFinite || !b.IsFinite) throw new InvalidOperationException("Bars require a finite baseline in the selected axis scale.");
                        FillPolygon(f,[a,new(b.X,a.Y),b,new(a.X,b.Y)],bar.Color,p);
                    }
                    break;
                case ErrorBarSeries error:
                    for (var i=0;i<error.Data.Count;i++)
                    {
                        var vx=error.Data.X(i); var vy=error.Data.Y(i);
                        var a=Map(new(vx,vy-error.LowerErrors.Span[i])); var b=Map(new(vx,vy+error.UpperErrors.Span[i]));
                        f.Line(a,b,error.Color,error.StrokeWidth,p);
                        f.Line(new(a.X-4,a.Y),new(a.X+4,a.Y),error.Color,error.StrokeWidth,p);
                        f.Line(new(b.X-4,b.Y),new(b.X+4,b.Y),error.Color,error.StrokeWidth,p);
                        f.Marker(Map(new(vx,vy)),MarkerShape.Circle,5,error.Color,p);
                    }
                    break;
                case FinancialSeries financial:
                    var points=financial.Points;
                    for (var i=0;i<points.Count;i++)
                    {
                        var candle=points[i]; var half=i+1<points.Count?Math.Abs(points[i+1].X-candle.X)*.32:i>0?Math.Abs(candle.X-points[i-1].X)*.32:.32;
                        var color=candle.Close>=candle.Open?Palette.Series[3]:Palette.Series[2];
                        f.Line(Map(new(candle.X,candle.Low)),Map(new(candle.X,candle.High)),color,financial.StrokeWidth,p);
                        var o=Map(new(candle.X-half,candle.Open)); var c=Map(new(candle.X+half,candle.Close));
                        if (financial.Style==FinancialStyle.Ohlc)
                        {
                            f.Line(o,Map(new(candle.X,candle.Open)),color,financial.StrokeWidth,p);
                            f.Line(Map(new(candle.X,candle.Close)),c,color,financial.StrokeWidth,p);
                        }
                        else if (Math.Abs(o.Y-c.Y)<1) f.Line(o,new(c.X,o.Y),color,1.5,p);
                        else FillPolygon(f,[o,new(c.X,o.Y),c,new(o.X,c.Y)],color,p);
                    }
                    break;
                case ContourSeries contour:
                    foreach (var segment in GeometryAlgorithms.Contours(contour.Grid,contour.Levels.Span))
                        f.Line(Map(segment.A),Map(segment.B),contour.ResolveColorScale().Color(segment.Level),contour.StrokeWidth,p);
                    break;
                case HeatmapSeries heat when s.GetType()==typeof(HeatmapSeries):
                    DrawHeatmap(heat, l, x, y, f);
                    break;
                default: throw new NotSupportedException($"{s.GetType().Name} cannot be rendered in the XY view.");
            }
            f.Batch(start,p);
        }
        foreach(var a in d.Annotations)
        {
            var y=d.FindY(a.YAxisId); var p=l.ForAxis(y.Id); var q=l.ToScreen(d.XAxis,y,new(a.X,a.Y));
            var r=l.ToScreen(d.XAxis,y,new(a.X2,a.Y2)); start=f.Count;
            switch(a.Kind)
            {
                case AnnotationKind.Text: if(p.Contains(q)) f.Text(a.Text,q.X,q.Y,a.Color); break;
                case AnnotationKind.HorizontalLine: f.Line(new(p.X,q.Y),new(p.Right,q.Y),a.Color,1.5,p); break;
                case AnnotationKind.VerticalLine: f.Line(new(q.X,p.Y),new(q.X,p.Bottom),a.Color,1.5,p); break;
                case AnnotationKind.Rectangle:
                    FillPolygon(f,[q,new(r.X,q.Y),r,new(q.X,r.Y)],a.Color.WithAlpha(.15f),p);
                    f.Line(q,new(r.X,q.Y),a.Color,1,p); f.Line(new(r.X,q.Y),r,a.Color,1,p);
                    f.Line(r,new(q.X,r.Y),a.Color,1,p); f.Line(new(q.X,r.Y),q,a.Color,1,p); break;
                default: throw new NotSupportedException($"Unsupported annotation {a.Kind}.");
            }
            f.Batch(start,p);
        }
    }

    private void DrawLine(FrameBuilder f, LineSeries line, Axis x, RectD clip, Func<PointD,PointD> map)
    {
        var selected=_indexes.GetValue(line.Data,d=>new EnvelopeIndex(d)).Select(x,(int)Math.Ceiling(clip.Width));
        IReadOnlyList<PointD> points=selected.Select(i=>new PointD(line.Data.X(i),line.Data.Y(i))).ToArray();
        if(line.Interpolation==LineInterpolation.MonotoneCubic)
            points=GeometryAlgorithms.Monotone(XyData.FromPoints(points),8);
        PointD? previous=null;
        var baseline=map(new(x.Range.Minimum,line.Baseline)).Y;
        if(line.FillToBaseline&&!double.IsFinite(baseline)) throw new InvalidOperationException("Area baseline is outside the axis scale domain.");
        foreach(var source in points)
        {
            var q=map(source);
            if(!q.IsFinite) { previous=null; continue; }
            if(previous is { } a)
            {
                void Segment(PointD from,PointD to)
                {
                    if(line.FillToBaseline) FillBand(f,from,to,new(to.X,baseline),new(from.X,baseline),line.Color.WithAlpha(.25f),clip);
                    if(line.ShowLine) f.Line(from,to,line.Color,line.StrokeWidth,clip);
                }
                if(line.Interpolation==LineInterpolation.StepBefore) { var elbow=new PointD(a.X,q.Y); Segment(a,elbow); Segment(elbow,q); }
                else if(line.Interpolation==LineInterpolation.StepAfter) { var elbow=new PointD(q.X,a.Y); Segment(a,elbow); Segment(elbow,q); }
                else Segment(a,q);
            }
            previous=q;
        }
        // Markers belong to actual samples, never to artificial cubic subdivision points.
        foreach(var i in selected) f.Marker(map(new(line.Data.X(i),line.Data.Y(i))),line.Marker,line.MarkerSize,line.Color,clip);
    }

    private static void BuildAxes(ChartDocument d, ChartLayout l, FrameBuilder f)
    {
        var full=new RectD(0,0,f.Width,f.Height); var start=f.Count;
        var near=0; var far=0;
        foreach(var a in d.YAxes)
        {
            var p=l.ForAxis(a.Id); var side=a.Side==AxisSide.Near;
            var offset=d.AxisLayout==AxisLayout.Layered?(side?near++:far++)*52:0;
            var at=side?p.X-offset:p.Right+offset;
            var drawGrid=d.AxisLayout!=AxisLayout.Layered||ReferenceEquals(a,d.YAxis);
            foreach(var tick in a.Ticks(Math.Clamp((int)(p.Height/65),2,12)))
            {
                var y=p.Bottom-tick.Unit*p.Height;
                if(drawGrid&&d.ShowGrid) f.Line(new(p.X,y),new(p.Right,y),d.Theme.Grid,1,p);
                f.Line(new(at+(side?-4:0),y),new(at+(side?0:4),y),d.Theme.Text,1,full);
                f.Text(tick.Label,at+(side?-9:9),y+4,d.Theme.Text,11,side?"right":"left");
            }
            f.Line(new(at,p.Y),new(at,p.Bottom),d.Theme.Grid,1,full);
            f.Text(AxisTitle(a),at+(side?-52:46),p.Y+p.Height/2,d.Theme.Text,12,"center",-90);
        }
        for(var i=0;i<d.XAxes.Count;i++)
        {
            var a=d.XAxes[i]; var at=l.Plot.Bottom+36*i;
            foreach(var tick in a.Ticks(Math.Clamp((int)(l.Plot.Width/100),2,15)))
            {
                var x=l.Plot.X+tick.Unit*l.Plot.Width;
                if(i==0&&d.ShowGrid) foreach(var p in l.AxisPlots.Values.Distinct()) f.Line(new(x,p.Y),new(x,p.Bottom),d.Theme.Grid,1,p);
                f.Line(new(x,at),new(x,at+4),d.Theme.Text,1,full);
                f.Text(tick.Label,x,at+18,d.Theme.Text,11,"center");
            }
            f.Text(AxisTitle(a),l.Plot.X+l.Plot.Width/2,at+39,d.Theme.Text,12,"center");
        }
        f.Batch(start,full);
    }
    private static string AxisTitle(Axis a)=>a.Units.Length==0?a.Title:$"{a.Title} ({a.Units})";

    private static void BuildCircular(ChartDocument d, ChartLayout l, FrameBuilder f)
    {
        var p=l.Plot; var center=new PointD(p.X+p.Width/2,p.Y+p.Height/2); var radius=Math.Min(p.Width,p.Height)/2;
        PointD Screen(PointD u)=>new(center.X+u.X*radius,center.Y-u.Y*radius);
        var start=f.Count;
        for(var i=0;i<180;i++) f.Triangle(center,Screen(GeometryAlgorithms.Polar(i*2,1)),Screen(GeometryAlgorithms.Polar((i+1)*2,1)),d.Theme.Plot);
        void Curve(Func<double,PointD> fn,double a,double b,int steps=180)
        {
            PointD? prev=null;
            for(var i=0;i<=steps;i++)
            {
                var u=fn(a+(b-a)*i/steps); var q=Screen(u);
                if(u.X*u.X+u.Y*u.Y<=1.00001&&prev is { } v) f.Line(v,q,d.Theme.Grid,1,p);
                prev=u.X*u.X+u.Y*u.Y<=1.00001?q:null;
            }
        }
        if(d.ShowGrid)
        {
            if(d.View==ChartView.Polar)
            {
                for(var r=1;r<=5;r++) { var rr=r/5.0; Curve(t=>GeometryAlgorithms.Polar(t,rr),0,360); }
                for(var angle=0;angle<360;angle+=30)
                {
                    f.Line(center,Screen(GeometryAlgorithms.Polar(angle,1)),d.Theme.Grid,1,p);
                    var label=Screen(GeometryAlgorithms.Polar(angle,1.07)); f.Text(angle+"°",label.X,label.Y+4,d.Theme.Text,11,"center");
                }
                for(var r=1;r<=5;r++) f.Text(d.YAxis.Format(d.YAxis.FromUnit(r/5.0)),center.X+4,center.Y-r/5.0*radius+12,d.Theme.Text,10);
            }
            else
            {
                Curve(t=>GeometryAlgorithms.Polar(t,1),0,360);
                foreach(var r in new double[]{0,.2,.5,1,2,5}) { var resistance=r; Curve(t=>GeometryAlgorithms.Smith(resistance,Math.Tan(t)),-Math.PI/2+.001,Math.PI/2-.001,360); }
                foreach(var reactance in new double[]{-5,-2,-1,-.5,-.2,.2,.5,1,2,5})
                { var x=reactance; Curve(t=>GeometryAlgorithms.Smith(t/(1-t),x),0,.999,180); }
                f.Line(Screen(new(-1,0)),Screen(new(1,0)),d.Theme.Grid,1,p);
                f.Text("Normalized impedance • z = r + jx",center.X,p.Bottom+22,d.Theme.Text,11,"center");
            }
        }
        f.Batch(start,new(0,0,f.Width,f.Height));
        foreach(var s in d.Series.Where(s=>s.Visible))
        {
            if(s is not LineSeries line) throw new NotSupportedException("Polar and Smith views currently accept LineSeries only.");
            if(line.FillToBaseline||line.Interpolation!=LineInterpolation.Linear) throw new NotSupportedException("Circular views require linear unfilled series.");
            start=f.Count; PointD? previous=null;
            for(var i=0;i<line.Data.Count;i++)
            {
                var unit=d.View==ChartView.Polar?GeometryAlgorithms.Polar(line.Data.X(i),d.YAxis.ToUnit(line.Data.Y(i))):GeometryAlgorithms.Smith(line.Data.X(i),line.Data.Y(i));
                if(!unit.IsFinite||unit.X*unit.X+unit.Y*unit.Y>1+1e-10) { previous=null; continue; }
                var q=Screen(unit); if(line.ShowLine&&previous is { } a) f.Line(a,q,line.Color,line.StrokeWidth,p);
                f.Marker(q,line.Marker,line.MarkerSize,line.Color,p); previous=q;
            }
            f.Batch(start,p);
        }
        if(d.Annotations.Count>0) throw new NotSupportedException("Circular annotations are not implemented.");
    }

    private static void BuildLegend(ChartDocument d, ChartLayout l, FrameBuilder f)
    {
        var full=new RectD(0,0,f.Width,f.Height); var start=f.Count; var row=0;
        foreach(var s in d.Series)
        {
            var y=l.Plot.Y+10+row*23; if(y+20>l.Plot.Bottom) break;
            var x=Math.Max(l.Plot.X,l.Plot.Right-172); var color=s.Visible?s.Color:d.Theme.Grid;
            f.Rect(new(x-7,y-2,174,21),d.Theme.Background.WithAlpha(.9f));
            f.Line(new(x,y+8),new(x+20,y+8),color,3,full);
            f.Text(s.Title,x+27,y+12,s.Visible?d.Theme.Text:d.Theme.Text.WithAlpha(.4f),11);
            f.Hits.Add(new("legend",s.Id,new(x-7,y-2,174,21))); row++;
        }
        f.Batch(start,full);
    }

    // Split crossing boundaries before triangulation: avoids bow-tie area/band polygons.
    internal static void FillBand(FrameBuilder f,PointD a,PointD b,PointD c,PointD e,ColorRgba color,RectD clip)
    {
        if(!a.IsFinite||!b.IsFinite||!c.IsFinite||!e.IsFinite) return;
        var left=a.Y-e.Y; var right=b.Y-c.Y;
        if(Math.Sign(left)!=Math.Sign(right)&&left!=0&&right!=0)
        {
            var t=left/(left-right); var crossing=new PointD(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
            FillPolygon(f,[a,crossing,e],color,clip); FillPolygon(f,[crossing,b,c],color,clip);
        }
        else FillPolygon(f,[a,b,c,e],color,clip);
    }
    /// <summary>Sutherland-Hodgman clipping in double precision, before packing float geometry.</summary>
    internal static void FillPolygon(FrameBuilder f,IReadOnlyList<PointD> points,ColorRgba color,RectD clip)
    {
        if(points.Any(p=>!p.IsFinite)) return;
        var polygon=points.ToList();
        for(var edge=0;edge<4;edge++)
        {
            if(polygon.Count==0) return;
            var input=polygon; polygon=[];
            bool Inside(PointD p)=>edge switch { 0=>p.X>=clip.X,1=>p.X<=clip.Right,2=>p.Y>=clip.Y,_=>p.Y<=clip.Bottom };
            PointD Intersect(PointD a,PointD b)
            {
                if(edge<2) { var x=edge==0?clip.X:clip.Right; var t=(x-a.X)/(b.X-a.X); return new(x,a.Y+t*(b.Y-a.Y)); }
                var y=edge==2?clip.Y:clip.Bottom; var u=(y-a.Y)/(b.Y-a.Y); return new(a.X+u*(b.X-a.X),y);
            }
            var a=input[^1]; var insideA=Inside(a);
            foreach(var b in input)
            {
                var insideB=Inside(b);
                if(insideA!=insideB) polygon.Add(Intersect(a,b));
                if(insideB) polygon.Add(b);
                a=b; insideA=insideB;
            }
        }
        for(var i=1;i<polygon.Count-1;i++) f.Triangle(polygon[0],polygon[i],polygon[i+1],color);
    }
}
