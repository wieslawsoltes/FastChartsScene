using FastChartsScene.Core;
namespace FastChartsScene.Charts;

/// <summary>Computes a complete fit plan before changing any axis. Includes stacks, bar widths,
/// uncertainty intervals, 3D heights and Z extents. No partial viewport is applied on invalid data.</summary>
public static class ChartAutoFit
{
    private sealed class Bounds(Axis axis)
    {
        public Axis Axis { get; }=axis;
        public double Minimum=double.PositiveInfinity,Maximum=double.NegativeInfinity;
        public void Include(double value)
        {
            if(double.IsNaN(value)||Axis.Scale==AxisScale.Logarithmic&&value<=0) return;
            if(!double.IsFinite(value)) throw new InvalidOperationException($"Data extent overflows axis '{Axis.Id}'.");
            Minimum=Math.Min(Minimum,value); Maximum=Math.Max(Maximum,value);
        }
        public AxisRange? Range()
        {
            if(!double.IsFinite(Minimum)) return null;
            if(Axis.Scale!=AxisScale.Logarithmic) return AxisRange.FromData(Minimum,Maximum);
            var min=Math.Log10(Minimum); var max=Math.Log10(Maximum); var pad=Math.Max((max-min)*.05,.05);
            var a=Math.Pow(10,min-pad); var b=Math.Pow(10,max+pad);
            if(a==0) a=double.Epsilon; if(double.IsPositiveInfinity(b)) b=double.MaxValue;
            return new(a,b);
        }
    }
    public static void Apply(ChartDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors=document.Validate(); if(errors.Count>0) throw new InvalidOperationException(string.Join("\n",errors));
        if(document.View is ChartView.Smith or ChartView.Pie3D) return;
        var bounds=document.XAxes.Concat(document.YAxes).Append(document.ZAxis).Distinct().ToDictionary(a=>a,a=>new Bounds(a));
        var stacks=new Dictionary<(string X,string Y,string Group,bool Horizontal,double Category,bool Positive),double>();
        foreach(var s in document.Series.Where(s=>s.Visible))
        {
            var x=bounds[document.FindX(s.XAxisId)]; var y=bounds[document.FindY(s.YAxisId)]; var z=bounds[document.ZAxis];
            if(document.View==ChartView.Polar)
            {
                if(s is DataSeries polar) { y.Include(0); for(var i=0;i<polar.Data.Count;i++) y.Include(polar.Data.Y(i)); }
                continue;
            }
            switch(s)
            {
                case BarSeries bar:
                    var category=bar.Horizontal?y:x; var value=bar.Horizontal?x:y;
                    for(var i=0;i<bar.Data.Count;i++)
                    {
                        var c=bar.Data.X(i); var v=bar.Data.Y(i); if(double.IsNaN(v)) continue;
                        var baseline=bar.Baseline;
                        if(bar.StackGroup.Length>0)
                        {
                            var key=(bar.XAxisId,bar.YAxisId,bar.StackGroup,bar.Horizontal,c,v>=0);
                            baseline=stacks.GetValueOrDefault(key,bar.Baseline); v+=baseline; stacks[key]=v;
                        }
                        category.Include(c-bar.BarWidth/2); category.Include(c+bar.BarWidth/2);
                        value.Include(baseline); value.Include(v);
                    }
                    break;
                case DataSeries data:
                    for(var i=0;i<data.Data.Count;i++)
                    {
                        x.Include(data.Data.X(i)); var v=data.Data.Y(i); y.Include(v);
                        if(data is BandSeries band) y.Include(band.Upper.Y(i));
                        if(data is ErrorBarSeries error) { y.Include(v-error.LowerErrors.Span[i]); y.Include(v+error.UpperErrors.Span[i]); }
                    }
                    if(data is LineSeries { FillToBaseline:true } line) y.Include(line.Baseline);
                    break;
                case FinancialSeries finance:
                    var points=finance.Points;
                    for(var i=0;i<points.Count;i++)
                    {
                        var p=points[i]; var half=i+1<points.Count?Math.Abs(points[i+1].X-p.X)*.32:i>0?Math.Abs(p.X-points[i-1].X)*.32:.32;
                        x.Include(p.X-half); x.Include(p.X+half); y.Include(p.Low); y.Include(p.High);
                    }
                    break;
                case HeatmapSeries heat:
                    x.Include(heat.Grid.XMinimum); x.Include(heat.Grid.XMaximum);
                    if(document.View==ChartView.ThreeD)
                    {
                        y.Include(heat.Grid.ValueRange.Minimum); y.Include(heat.Grid.ValueRange.Maximum);
                        if(heat is BarSeries3D) y.Include(0);
                        z.Include(heat.Grid.YMinimum); z.Include(heat.Grid.YMaximum);
                    }
                    else { y.Include(heat.Grid.YMinimum); y.Include(heat.Grid.YMaximum); }
                    break;
                case PointLineSeries3D line:
                    foreach(var p in line.Points.Span) { x.Include(p.X); y.Include(p.Y); z.Include(p.Z); }
                    break;
                case MeshSeries3D mesh:
                    foreach(var v in mesh.Mesh.Vertices.Span) { x.Include(v.Position.X); y.Include(v.Position.Y); z.Include(v.Position.Z); }
                    break;
            }
        }
        var plan=bounds.Values.Select(b=>(b.Axis,Range:b.Range())).Where(p=>p.Range!=null).ToArray();
        using var update=document.BeginUpdate(); foreach(var p in plan) p.Axis.Range=p.Range!.Value;
        if(document.View==ChartView.Polar&&document.YAxis.Scale!=AxisScale.Logarithmic)
            document.YAxis.Range=new(0,Math.Max(double.Epsilon,document.YAxis.Range.Maximum));
    }
}
