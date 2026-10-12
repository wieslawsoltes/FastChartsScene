using System.Text;
using System.Text.Json;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;

namespace FastChartsScene.Serialization;

public sealed record ChartReadLimits(int MaximumCharacters=64*1024*1024,int MaximumNumericValues=4_000_000,int MaximumSeries=4096,int MaximumAxes=256,int MaximumAnnotations=16384);

/// <summary>Versioned, reflection-free chart persistence suitable for trimmed and WebAssembly builds.
/// No type names, executable payloads, external file references or network locations are activated.
/// Series IDs are session identities and are regenerated on import; order and axis bindings are preserved.
/// NaN samples use JSON null. Floating-point values and colors round-trip without quantization.</summary>
public static partial class ChartJson
{
    public const int CurrentVersion=1;
    public static string Write(ChartDocument document,bool indented=false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors=document.Validate(); if(errors.Count>0) throw new InvalidOperationException(string.Join("\n",errors));
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream,new JsonWriterOptions { Indented=indented }))
        {
            w.WriteStartObject(); w.WriteString("format","FastChartsScene"); w.WriteNumber("version",CurrentVersion);
            w.WriteString("title",document.Title); w.WriteString("subtitle",document.Subtitle); w.WriteString("view",document.View.ToString()); w.WriteString("layout",document.AxisLayout.ToString());
            w.WriteBoolean("showGrid",document.ShowGrid); w.WriteBoolean("showLegend",document.ShowLegend);
            w.WriteStartObject("theme"); Color(w,"background",document.Theme.Background); Color(w,"plot",document.Theme.Plot); Color(w,"grid",document.Theme.Grid); Color(w,"text",document.Theme.Text); Color(w,"accent",document.Theme.Accent); w.WriteEndObject();
            w.WriteStartObject("camera"); w.WriteNumber("azimuth",document.Camera.Azimuth); w.WriteNumber("elevation",document.Camera.Elevation); w.WriteNumber("distance",document.Camera.Distance); w.WriteBoolean("orthographic",document.Camera.Orthographic); w.WriteEndObject();
            w.WriteStartArray("xAxes"); foreach(var a in document.XAxes) Axis(w,a); w.WriteEndArray();
            w.WriteStartArray("yAxes"); foreach(var a in document.YAxes) Axis(w,a); w.WriteEndArray();
            w.WritePropertyName("zAxis"); Axis(w,document.ZAxis);
            w.WriteStartArray("segments"); foreach(var s in document.Segments)
            {
                w.WriteStartObject(); w.WriteString("name",s.Name); w.WriteNumber("weight",s.Weight); w.WriteStartArray("axes"); foreach(var id in s.AxisIds) w.WriteStringValue(id); w.WriteEndArray(); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteStartArray("annotations"); foreach(var a in document.Annotations)
            {
                w.WriteStartObject(); w.WriteString("kind",a.Kind.ToString()); w.WriteString("text",a.Text); w.WriteString("yAxis",a.YAxisId);
                w.WriteNumber("x",a.X); w.WriteNumber("y",a.Y); w.WriteNumber("x2",a.X2); w.WriteNumber("y2",a.Y2); Color(w,"color",a.Color); w.WriteEndObject();
            }
            w.WriteEndArray(); w.WriteStartArray("series"); foreach(var s in document.Series) Series(w,s); w.WriteEndArray(); w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(),0,checked((int)stream.Length));
    }
    private static void Axis(Utf8JsonWriter w,Axis a)
    {
        w.WriteStartObject(); w.WriteString("id",a.Id); w.WriteString("title",a.Title); w.WriteString("units",a.Units);
        w.WriteNumber("minimum",a.Range.Minimum); w.WriteNumber("maximum",a.Range.Maximum); w.WriteString("scale",a.Scale.ToString()); w.WriteString("side",a.Side.ToString());
        w.WriteBoolean("inverted",a.Inverted); w.WriteNumber("weight",a.Weight); w.WriteStartArray("categories"); foreach(var label in a.Categories) w.WriteStringValue(label); w.WriteEndArray(); WriteTickOptions(w,a); w.WriteEndObject();
    }
    private static void Color(Utf8JsonWriter w,string name,ColorRgba c)
    {
        w.WriteStartArray(name); foreach(var v in new[]{c.R,c.G,c.B,c.A}) { if(!float.IsFinite(v)||v<0||v>1) throw new InvalidOperationException("Color components must be finite and in [0,1]."); w.WriteNumberValue(v); } w.WriteEndArray();
    }
    private static void Number(Utf8JsonWriter w,double value) { if(double.IsNaN(value)) w.WriteNullValue(); else w.WriteNumberValue(value); }
    private static void Numbers(Utf8JsonWriter w,string name,ReadOnlySpan<double> values)
    { w.WriteStartArray(name); foreach(var v in values) Number(w,v); w.WriteEndArray(); }
    private static void Data(Utf8JsonWriter w,string name,IDataSource data)
    {
        w.WriteStartObject(name);
        if(data is UniformData u) { w.WriteString("kind","uniform"); w.WriteNumber("start",u.Start); w.WriteNumber("step",u.Step); }
        else { w.WriteString("kind","xy"); w.WriteStartArray("x"); for(var i=0;i<data.Count;i++) w.WriteNumberValue(data.X(i)); w.WriteEndArray(); }
        w.WriteStartArray("y"); for(var i=0;i<data.Count;i++) Number(w,data.Y(i)); w.WriteEndArray(); w.WriteEndObject();
    }
    private static void Grid(Utf8JsonWriter w,ScalarGrid g)
    {
        w.WriteStartObject("grid"); w.WriteNumber("columns",g.Columns); w.WriteNumber("rows",g.Rows);
        w.WriteNumber("xMinimum",g.XMinimum); w.WriteNumber("xMaximum",g.XMaximum); w.WriteNumber("yMinimum",g.YMinimum); w.WriteNumber("yMaximum",g.YMaximum); Numbers(w,"values",g.Values.Span); w.WriteEndObject();
    }
    private static void Position(Utf8JsonWriter w,Point3D p) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(p.Z); w.WriteEndArray(); }
    private static void Series(Utf8JsonWriter w,Series s)
    {
        var kind=s switch { LineSeries=>"line",BandSeries=>"band",BarSeries=>"bar",ErrorBarSeries=>"error",FinancialSeries=>"financial",ContourSeries=>"contour",SurfaceSeries3D=>"surface",WaterfallSeries3D=>"waterfall",BarSeries3D=>"bars3d",PointLineSeries3D=>"points3d",MeshSeries3D=>"mesh",PieSeries=>"pie",HeatmapSeries when s.GetType()==typeof(HeatmapSeries)=>"heatmap",_=>throw new NotSupportedException("Unsupported series type: "+s.GetType().Name) };
        w.WriteStartObject(); w.WriteString("kind",kind); w.WriteString("title",s.Title); w.WriteBoolean("visible",s.Visible); w.WriteString("xAxis",s.XAxisId); w.WriteString("yAxis",s.YAxisId); w.WriteNumber("strokeWidth",s.StrokeWidth); Color(w,"color",s.Color);
        if(s is DataSeries data) Data(w,"data",data.Data);
        if(s is HeatmapSeries heat) { Grid(w,heat.Grid); WriteColorSettings(w,heat); }
        switch(s)
        {
            case LineSeries line:
                w.WriteString("interpolation",line.Interpolation.ToString()); w.WriteString("marker",line.Marker.ToString()); w.WriteNumber("markerSize",line.MarkerSize); w.WriteBoolean("showLine",line.ShowLine); w.WriteBoolean("fillToBaseline",line.FillToBaseline); w.WriteNumber("baseline",line.Baseline); break;
            case BandSeries band: Data(w,"upper",band.Upper); break;
            case BarSeries bar: w.WriteBoolean("horizontal",bar.Horizontal); w.WriteNumber("barWidth",bar.BarWidth); w.WriteNumber("baseline",bar.Baseline); w.WriteString("stackGroup",bar.StackGroup); break;
            case ErrorBarSeries error: Numbers(w,"lowerErrors",error.LowerErrors.Span); Numbers(w,"upperErrors",error.UpperErrors.Span); break;
            case ContourSeries contour: Numbers(w,"levels",contour.Levels.Span); break;
            case FinancialSeries financial:
                w.WriteString("style",financial.Style.ToString()); w.WriteStartArray("candles"); foreach(var p in financial.Points) { w.WriteStartArray(); foreach(var n in new[]{p.X,p.Open,p.High,p.Low,p.Close,p.Volume}) w.WriteNumberValue(n); w.WriteEndArray(); } w.WriteEndArray(); break;
            case PointLineSeries3D spatial:
                w.WriteBoolean("showLine",spatial.ShowLine); w.WriteNumber("pointSize",spatial.PointSize); w.WriteStartArray("points"); foreach(var p in spatial.Points.Span) Position(w,p); w.WriteEndArray(); break;
            case MeshSeries3D mesh:
                w.WriteStartArray("vertices"); foreach(var v in mesh.Mesh.Vertices.Span) { w.WriteStartObject(); w.WritePropertyName("position"); Position(w,v.Position); Color(w,"color",v.Color); w.WriteEndObject(); } w.WriteEndArray();
                w.WriteStartArray("indices"); foreach(var i in mesh.Mesh.Indices.Span) w.WriteNumberValue(i); w.WriteEndArray(); break;
            case PieSeries pie:
                w.WriteNumber("innerRadius",pie.InnerRadius); w.WriteNumber("height",pie.Height); w.WriteNumber("explosion",pie.Explosion); w.WriteStartArray("slices"); foreach(var slice in pie.Slices.Span) { w.WriteStartObject(); w.WriteString("label",slice.Label); w.WriteNumber("value",slice.Value); Color(w,"color",slice.Color); w.WriteEndObject(); } w.WriteEndArray(); break;
        }
        w.WriteEndObject();
    }

    public static ChartDocument Read(string json,ChartReadLimits? limits=null)
    {
        ArgumentNullException.ThrowIfNull(json); limits??=new();
        if(limits.MaximumCharacters<1||limits.MaximumNumericValues<1||limits.MaximumSeries<1||limits.MaximumAxes<1||limits.MaximumAnnotations<1) throw new ArgumentOutOfRangeException(nameof(limits));
        if(json.Length>limits.MaximumCharacters) throw new FormatException("Chart document exceeds the configured input size limit.");
        ChartDocument? document=null;
        try
        {
            using var parsed=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=64,CommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false });
            var root=parsed.RootElement; RejectDuplicateProperties(root);
            if(Text(root,"format")!="FastChartsScene"||root.GetProperty("version").GetInt32()!=CurrentVersion) throw new FormatException("Unsupported chart document format or version.");
            var r=new Reader(limits); document=new ChartDocument();
            using var update=document.BeginUpdate();
            document.Title=Text(root,"title"); document.Subtitle=Text(root,"subtitle"); document.View=EnumValue<ChartView>(root,"view"); document.AxisLayout=EnumValue<AxisLayout>(root,"layout"); document.ShowGrid=root.GetProperty("showGrid").GetBoolean(); document.ShowLegend=root.GetProperty("showLegend").GetBoolean();
            var theme=root.GetProperty("theme"); document.Theme=new(r.Color(theme,"background"),r.Color(theme,"plot"),r.Color(theme,"grid"),r.Color(theme,"text"),r.Color(theme,"accent"));
            document.XAxes.Clear(); foreach(var e in r.Items(root.GetProperty("xAxes"),limits.MaximumAxes)) document.XAxes.Add(r.Axis(e));
            document.YAxes.Clear(); foreach(var e in r.Items(root.GetProperty("yAxes"),limits.MaximumAxes)) document.YAxes.Add(r.Axis(e));
            r.ApplyAxis(root.GetProperty("zAxis"),document.ZAxis);
            var camera=root.GetProperty("camera"); document.Camera.Azimuth=r.Number(camera,"azimuth"); document.Camera.Elevation=r.Number(camera,"elevation"); document.Camera.Distance=r.Number(camera,"distance"); document.Camera.Orthographic=camera.GetProperty("orthographic").GetBoolean();
            foreach(var e in r.Items(root.GetProperty("segments"),limits.MaximumAxes)) document.Segments.Add(new(Text(e,"name"),r.Number(e,"weight"),r.Strings(e.GetProperty("axes"),limits.MaximumAxes)));
            foreach(var e in r.Items(root.GetProperty("annotations"),limits.MaximumAnnotations)) document.Annotations.Add(new Annotation { Kind=EnumValue<AnnotationKind>(e,"kind"),Text=Text(e,"text"),YAxisId=Text(e,"yAxis"),X=r.Number(e,"x"),Y=r.Number(e,"y"),X2=r.Number(e,"x2"),Y2=r.Number(e,"y2"),Color=r.Color(e,"color") });
            foreach(var e in r.Items(root.GetProperty("series"),limits.MaximumSeries)) document.Series.Add(r.Series(e));
            var errors=document.Validate(); if(errors.Count>0) throw new FormatException(string.Join("\n",errors));
            return document;
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or OverflowException or FormatException)
        {
            document?.Dispose(); throw new FormatException("Invalid chart document: "+e.Message,e);
        }
    }
    private static void RejectDuplicateProperties(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in e.EnumerateObject()) { if(!names.Add(p.Name)) throw new FormatException("Duplicate JSON property: "+p.Name); RejectDuplicateProperties(p.Value); }
        }
        else if(e.ValueKind==JsonValueKind.Array) foreach(var item in e.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private static string Text(JsonElement e,string name)=>e.GetProperty(name).GetString()??throw new FormatException("Null string: "+name);
    private static T EnumValue<T>(JsonElement e,string name) where T:struct,Enum
    {
        var text=Text(e,name);
        if(!Enum.TryParse<T>(text,false,out var result)||!Enum.IsDefined(result)||result.ToString()!=text) throw new FormatException("Invalid "+name+": "+text);
        return result;
    }
    private sealed partial class Reader(ChartReadLimits limits)
    {
        private int _numericValues;
        public JsonElement.ArrayEnumerator Items(JsonElement e,int maximum)
        {
            if(e.ValueKind!=JsonValueKind.Array||e.GetArrayLength()>maximum) throw new FormatException("Array exceeds its configured length limit or is not an array.");
            return e.EnumerateArray();
        }
        public string[] Strings(JsonElement e,int maximum)=>Items(e,maximum).Select(v=>v.GetString()??throw new FormatException("Null array string.")).ToArray();
        public double Number(JsonElement e,string name)=>Number(e.GetProperty(name));
        private double Number(JsonElement e,bool allowGap=false)
        {
            if(++_numericValues>limits.MaximumNumericValues) throw new FormatException("Numeric data budget exceeded.");
            if(allowGap&&e.ValueKind==JsonValueKind.Null) return double.NaN;
            var value=e.GetDouble(); if(!double.IsFinite(value)) throw new FormatException("Non-finite numeric value."); return value;
        }
        private int Integer(JsonElement e)
        {
            var n=Number(e); if(n<int.MinValue||n>int.MaxValue||Math.Truncate(n)!=n) throw new FormatException("Expected an integer."); return (int)n;
        }
        private double[] Numbers(JsonElement e,bool allowGaps=false)
        {
            var items=Items(e,limits.MaximumNumericValues-_numericValues); var values=new double[e.GetArrayLength()]; var i=0;
            foreach(var item in items) values[i++]=Number(item,allowGaps); return values;
        }
        private double[] Tuple(JsonElement e,int length)
        { if(e.GetArrayLength()!=length) throw new FormatException("Unexpected tuple length."); return Numbers(e); }
        public ColorRgba Color(JsonElement e,string name)
        {
            var v=Tuple(e.GetProperty(name),4); if(v.Any(n=>n<0||n>1)) throw new FormatException("Color outside [0,1]."); return new((float)v[0],(float)v[1],(float)v[2],(float)v[3]);
        }
        public Axis Axis(JsonElement e) { var axis=new Axis(Text(e,"id")); ApplyAxis(e,axis); return axis; }
        public void ApplyAxis(JsonElement e,Axis axis)
        {
            if(axis.Id!=Text(e,"id")) throw new FormatException("Axis identity mismatch.");
            axis.Scale=AxisScale.Linear; axis.Range=new(Number(e,"minimum"),Number(e,"maximum")); axis.Scale=EnumValue<AxisScale>(e,"scale"); axis.Title=Text(e,"title"); axis.Units=Text(e,"units"); axis.Side=EnumValue<AxisSide>(e,"side"); axis.Inverted=e.GetProperty("inverted").GetBoolean(); axis.Weight=Number(e,"weight"); axis.Categories=Strings(e.GetProperty("categories"),100000); ReadTickOptions(e,axis);
        }
        private IDataSource Data(JsonElement e)
        {
            var y=Numbers(e.GetProperty("y"),true);
            return Text(e,"kind") switch { "uniform"=>new UniformData(Number(e,"start"),Number(e,"step"),y),"xy"=>new XyData(Numbers(e.GetProperty("x")),y),_=>throw new FormatException("Unknown data source kind.") };
        }
        private ScalarGrid Grid(JsonElement e)
        {
            var columns=Integer(e.GetProperty("columns")); var rows=Integer(e.GetProperty("rows"));
            if(columns<1||rows<1||(long)columns*rows>limits.MaximumNumericValues-_numericValues) throw new FormatException("Grid dimensions exceed the data budget.");
            var values=Numbers(e.GetProperty("values"),true);
            return new(columns,rows,Number(e,"xMinimum"),Number(e,"xMaximum"),Number(e,"yMinimum"),Number(e,"yMaximum"),values);
        }
        private Point3D Position(JsonElement e) { var v=Tuple(e,3); return new(v[0],v[1],v[2]); }
        public Series Series(JsonElement e)
        {
            Series series=Text(e,"kind") switch { "line"=>new LineSeries(),"band"=>new BandSeries(),"bar"=>new BarSeries(),"error"=>new ErrorBarSeries(),"financial"=>new FinancialSeries(),"heatmap"=>new HeatmapSeries(),"contour"=>new ContourSeries(),"surface"=>new SurfaceSeries3D(),"waterfall"=>new WaterfallSeries3D(),"bars3d"=>new BarSeries3D(),"points3d"=>new PointLineSeries3D(),"mesh"=>new MeshSeries3D(),"pie"=>new PieSeries(),_=>throw new FormatException("Unknown series kind.") };
            series.Title=Text(e,"title"); series.Visible=e.GetProperty("visible").GetBoolean(); series.XAxisId=Text(e,"xAxis"); series.YAxisId=Text(e,"yAxis"); series.StrokeWidth=Number(e,"strokeWidth"); series.Color=Color(e,"color");
            if(series is DataSeries data) data.Data=Data(e.GetProperty("data"));
            if(series is HeatmapSeries heat) { heat.Grid=Grid(e.GetProperty("grid")); ReadColorSettings(e,heat); }
            switch(series)
            {
                case LineSeries line:
                    line.Interpolation=EnumValue<LineInterpolation>(e,"interpolation"); line.Marker=EnumValue<MarkerShape>(e,"marker"); line.MarkerSize=Number(e,"markerSize"); line.ShowLine=e.GetProperty("showLine").GetBoolean(); line.FillToBaseline=e.GetProperty("fillToBaseline").GetBoolean(); line.Baseline=Number(e,"baseline"); break;
                case BandSeries band: band.Upper=Data(e.GetProperty("upper")); break;
                case BarSeries bar: bar.Horizontal=e.GetProperty("horizontal").GetBoolean(); bar.BarWidth=Number(e,"barWidth"); bar.Baseline=Number(e,"baseline"); bar.StackGroup=Text(e,"stackGroup"); break;
                case ErrorBarSeries error: error.SetErrors(Numbers(e.GetProperty("lowerErrors")),Numbers(e.GetProperty("upperErrors"))); break;
                case ContourSeries contour: contour.SetLevels(Numbers(e.GetProperty("levels"))); break;
                case FinancialSeries financial:
                    financial.Style=EnumValue<FinancialStyle>(e,"style"); financial.SetData(Items(e.GetProperty("candles"),limits.MaximumNumericValues/6).Select(item=> { var v=Tuple(item,6); return new OhlcPoint(v[0],v[1],v[2],v[3],v[4],v[5]); }).ToArray()); break;
                case PointLineSeries3D spatial:
                    spatial.ShowLine=e.GetProperty("showLine").GetBoolean(); spatial.PointSize=Number(e,"pointSize"); spatial.SetData(Items(e.GetProperty("points"),limits.MaximumNumericValues/3).Select(Position).ToArray()); break;
                case MeshSeries3D mesh:
                    var vertices=Items(e.GetProperty("vertices"),limits.MaximumNumericValues/7).Select(item=>new MeshVertex(Position(item.GetProperty("position")),Color(item,"color"))).ToArray();
                    var indices=Items(e.GetProperty("indices"),limits.MaximumNumericValues-_numericValues).Select(Integer).ToArray(); mesh.Mesh=new(vertices,indices); break;
                case PieSeries pie:
                    pie.InnerRadius=Number(e,"innerRadius"); pie.Height=Number(e,"height"); pie.Explosion=Number(e,"explosion");
                    pie.SetData(Items(e.GetProperty("slices"),limits.MaximumNumericValues/5).Select(item=>new PieSlice(Text(item,"label"),Number(item,"value"),Color(item,"color"))).ToArray()); break;
            }
            return series;
        }
    }
}
