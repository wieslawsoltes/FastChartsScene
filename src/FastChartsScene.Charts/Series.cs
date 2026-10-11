using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Charts;

public enum ChartView { XY, Polar, Smith, ThreeD, Pie3D }
public enum LineInterpolation { Linear, StepBefore, StepAfter, MonotoneCubic }
public enum MarkerShape { None, Circle, Square, Diamond, Triangle, Cross }
public enum FinancialStyle { Candlestick, Ohlc }
public enum AxisLayout { Layered, Stacked, Segmented }

public abstract class Series : ObservableModel
{
    private string _title="Series",_xAxisId="x",_yAxisId="y";
    private bool _visible=true;
    private ColorRgba _color=Palette.Series[0];
    private double _strokeWidth=1.5;
    public string Id { get; }=Guid.NewGuid().ToString("N");
    public string Title { get=>_title; set=>Set(ref _title,value??""); }
    public bool Visible { get=>_visible; set=>Set(ref _visible,value); }
    public ColorRgba Color { get=>_color; set=>Set(ref _color,value); }
    public string XAxisId { get=>_xAxisId; set { ArgumentException.ThrowIfNullOrWhiteSpace(value); Set(ref _xAxisId,value); } }
    public string YAxisId { get=>_yAxisId; set { ArgumentException.ThrowIfNullOrWhiteSpace(value); Set(ref _yAxisId,value); } }
    public double StrokeWidth { get=>_strokeWidth; set { if(!double.IsFinite(value)||value<0||value>64) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _strokeWidth,value); } }
    public abstract int PointCount { get; }
}

public abstract class DataSeries : Series
{
    private IDataSource _data=new XyData([],[]);
    public IDataSource Data { get=>_data; set { ArgumentNullException.ThrowIfNull(value); Set(ref _data,value); } }
    public override int PointCount=>Data.Count;
}

public sealed class LineSeries : DataSeries
{
    private LineInterpolation _interpolation;
    private MarkerShape _marker;
    private bool _showLine=true,_fill;
    private double _baseline,_markerSize=5;
    public LineInterpolation Interpolation { get=>_interpolation; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _interpolation,value); } }
    public MarkerShape Marker { get=>_marker; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _marker,value); } }
    public bool ShowLine { get=>_showLine; set=>Set(ref _showLine,value); }
    public bool FillToBaseline { get=>_fill; set=>Set(ref _fill,value); }
    public double Baseline { get=>_baseline; set { if(!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _baseline,value); } }
    public double MarkerSize { get=>_markerSize; set { if(!double.IsFinite(value)||value<=0||value>128) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _markerSize,value); } }
}

public sealed class BandSeries : DataSeries
{
    private IDataSource _upper=new XyData([],[]);
    public IDataSource Upper { get=>_upper; set { ArgumentNullException.ThrowIfNull(value); Set(ref _upper,value); } }
}

public sealed class BarSeries : DataSeries
{
    private double _width=0.8,_baseline;
    private bool _horizontal;
    private string _stackGroup="";
    public double BarWidth { get=>_width; set { if(!double.IsFinite(value)||value<=0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _width,value); } }
    public double Baseline { get=>_baseline; set { if(!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _baseline,value); } }
    public bool Horizontal { get=>_horizontal; set=>Set(ref _horizontal,value); }
    public string StackGroup { get=>_stackGroup; set=>Set(ref _stackGroup,value??""); }
}

public readonly record struct OhlcPoint(double X,double Open,double High,double Low,double Close,double Volume=0);
public sealed class FinancialSeries : Series
{
    private OhlcPoint[] _points=[];
    private FinancialStyle _style;
    private IReadOnlyList<OhlcPoint> _readOnlyPoints=Array.AsReadOnly(Array.Empty<OhlcPoint>());
    public IReadOnlyList<OhlcPoint> Points=>_readOnlyPoints;
    public override int PointCount=>_points.Length;
    public FinancialStyle Style { get=>_style; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _style,value); } }
    public void SetData(ReadOnlySpan<OhlcPoint> points)
    {
        foreach(var p in points) if(!double.IsFinite(p.X)||!double.IsFinite(p.Open)||!double.IsFinite(p.High)||!double.IsFinite(p.Low)||!double.IsFinite(p.Close)||!double.IsFinite(p.Volume)||p.High<Math.Max(p.Open,p.Close)||p.Low>Math.Min(p.Open,p.Close)||p.Volume<0) throw new ArgumentException("Invalid OHLC candle.");
        _points=points.ToArray(); _readOnlyPoints=Array.AsReadOnly(_points); Notify(nameof(Points));
    }
}

public sealed class ErrorBarSeries : DataSeries
{
    private double[] _lower=[],_upper=[];
    public ReadOnlyMemory<double> LowerErrors=>_lower;
    public ReadOnlyMemory<double> UpperErrors=>_upper;
    public void SetErrors(ReadOnlySpan<double> lower,ReadOnlySpan<double> upper)
    {
        if(lower.Length!=upper.Length) throw new ArgumentException("Error array lengths differ.");
        foreach(var v in lower) if(!double.IsFinite(v)||v<0) throw new ArgumentException("Errors must be non-negative and finite.");
        foreach(var v in upper) if(!double.IsFinite(v)||v<0) throw new ArgumentException("Errors must be non-negative and finite.");
        _lower=lower.ToArray(); _upper=upper.ToArray(); Notify(nameof(LowerErrors));
    }
}

public sealed partial class ScalarGrid
{
    private readonly double[] _values;
    public int Columns { get; }
    public int Rows { get; }
    public double XMinimum { get; }
    public double XMaximum { get; }
    public double YMinimum { get; }
    public double YMaximum { get; }
    public ReadOnlyMemory<double> Values=>_values;
    public (double Minimum,double Maximum) ValueRange { get; }
    public ScalarGrid(int columns,int rows,double xmin,double xmax,double ymin,double ymax,ReadOnlySpan<double> values)
    {
        if(columns<1||rows<1||values.Length!=checked(columns*rows)) throw new ArgumentException("Grid dimensions do not match values.");
        _=new AxisRange(xmin,xmax); _=new AxisRange(ymin,ymax);
        Columns=columns; Rows=rows; XMinimum=xmin; XMaximum=xmax; YMinimum=ymin; YMaximum=ymax; _values=values.ToArray();
        var min=double.PositiveInfinity; var max=double.NegativeInfinity;
        foreach(var v in values) { if(double.IsInfinity(v)) throw new ArgumentException("Infinite grid value."); if(double.IsNaN(v)) continue; min=Math.Min(min,v); max=Math.Max(max,v); }
        ValueRange=double.IsFinite(min)?(min,max):(0,1);
    }
    public double this[int column,int row] { get { if((uint)column>=(uint)Columns||(uint)row>=(uint)Rows) throw new ArgumentOutOfRangeException(nameof(column)); return _values[row*Columns+column]; } }
    public double X(int column)=>Coordinate(XMinimum,XMaximum,column,Columns);
    public double Y(int row)=>Coordinate(YMinimum,YMaximum,row,Rows);
    public double Normalized(double value)=>ValueRange.Maximum==ValueRange.Minimum?0.5:(value-ValueRange.Minimum)/(ValueRange.Maximum-ValueRange.Minimum);
}

public partial class HeatmapSeries : Series
{
    private ScalarGrid _grid=new(1,1,0,1,0,1,[0]);
    public ScalarGrid Grid { get=>_grid; set { ArgumentNullException.ThrowIfNull(value); Set(ref _grid,value); } }
    public override int PointCount=>Grid.Columns*Grid.Rows;
}

public sealed class ContourSeries : HeatmapSeries
{
    private double[] _levels=[];
    public ReadOnlyMemory<double> Levels=>_levels;
    public void SetLevels(ReadOnlySpan<double> levels)
    {
        foreach(var v in levels) if(!double.IsFinite(v)) throw new ArgumentException("Contour levels must be finite.");
        _levels=levels.ToArray(); Array.Sort(_levels); Notify(nameof(Levels));
    }
}

public readonly record struct Point3D(double X,double Y,double Z);
public readonly record struct MeshVertex(Point3D Position,ColorRgba Color);
public sealed class MeshData
{
    private readonly MeshVertex[] _vertices;
    private readonly int[] _indices;
    public ReadOnlyMemory<MeshVertex> Vertices=>_vertices;
    public ReadOnlyMemory<int> Indices=>_indices;
    public MeshData(ReadOnlySpan<MeshVertex> vertices,ReadOnlySpan<int> indices)
    {
        if(indices.Length%3!=0) throw new ArgumentException("Mesh indices must describe triangles.");
        foreach(var v in vertices) if(!double.IsFinite(v.Position.X)||!double.IsFinite(v.Position.Y)||!double.IsFinite(v.Position.Z)) throw new ArgumentException("Mesh positions must be finite.");
        foreach(var i in indices) if(i<0||i>=vertices.Length) throw new ArgumentOutOfRangeException(nameof(indices));
        _vertices=vertices.ToArray(); _indices=indices.ToArray();
    }
}

public sealed class SurfaceSeries3D : HeatmapSeries { }
public sealed class WaterfallSeries3D : HeatmapSeries { }
public sealed class MeshSeries3D : Series
{
    private MeshData _mesh=new([],[]);
    public MeshData Mesh { get=>_mesh; set { ArgumentNullException.ThrowIfNull(value); Set(ref _mesh,value); } }
    public override int PointCount=>Mesh.Vertices.Length;
}
public sealed class PointLineSeries3D : Series
{
    private Point3D[] _points=[];
    private bool _showLine=true;
    private double _pointSize=4;
    public ReadOnlyMemory<Point3D> Points=>_points;
    public bool ShowLine { get=>_showLine; set=>Set(ref _showLine,value); }
    public double PointSize { get=>_pointSize; set { if(!double.IsFinite(value)||value<0||value>128) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _pointSize,value); } }
    public override int PointCount=>_points.Length;
    public void SetData(ReadOnlySpan<Point3D> points)
    {
        foreach(var p in points) if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z)) throw new ArgumentException("3D positions must be finite.");
        _points=points.ToArray(); Notify(nameof(Points));
    }
}
public sealed class BarSeries3D : HeatmapSeries { }

public readonly record struct PieSlice(string Label,double Value,ColorRgba Color);
public sealed class PieSeries : Series
{
    private PieSlice[] _slices=[];
    private double _innerRadius,_height=0.2,_explosion;
    public ReadOnlyMemory<PieSlice> Slices=>_slices;
    public double InnerRadius { get=>_innerRadius; set { if(!double.IsFinite(value)||value<0||value>=0.95) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _innerRadius,value); } }
    public double Height { get=>_height; set { if(!double.IsFinite(value)||value<0||value>2) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _height,value); } }
    public double Explosion { get=>_explosion; set { if(!double.IsFinite(value)||value<0||value>1) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _explosion,value); } }
    public override int PointCount=>_slices.Length;
    public void SetData(ReadOnlySpan<PieSlice> slices)
    {
        var sum=0.0; foreach(var s in slices) { if(!double.IsFinite(s.Value)||s.Value<0) throw new ArgumentException("Pie values must be finite and non-negative."); sum+=s.Value; }
        if(slices.Length>0&&(!double.IsFinite(sum)||sum<=0)) throw new ArgumentException("Pie sum must be positive and finite.");
        _slices=slices.ToArray(); Notify(nameof(Slices));
    }
}
