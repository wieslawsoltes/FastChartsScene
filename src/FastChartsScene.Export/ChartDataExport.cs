using System.Globalization;
using FastChartsScene.Charts;

namespace FastChartsScene.Export;

/// <summary>Long-form CSV for all supported series. A streaming writer avoids retaining a second copy
/// of large datasets. NaN is an empty numeric cell. Text is quoted and spreadsheet formulas are neutralized.</summary>
public static class ChartDataExport
{
    public const string Header="SeriesId,SeriesTitle,Kind,Index,X,Y,Z,YLow,YHigh,Open,High,Low,Close,Volume,Value,Label,Color,Vertex0,Vertex1,Vertex2";
    public static string WriteCsv(ChartDocument document,bool includeHidden=true)
    {
        using var writer=new StringWriter(CultureInfo.InvariantCulture); WriteCsv(document,writer,includeHidden); return writer.ToString();
    }
    public static void WriteCsv(ChartDocument document,TextWriter writer,bool includeHidden=true)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(writer);
        var errors=document.Validate(); if(errors.Count>0) throw new InvalidOperationException(string.Join("\n",errors));
        writer.Write(Header); writer.Write("\r\n");
        foreach(var s in document.Series.Where(s=>includeHidden||s.Visible))
        {
            void Row(string kind,int index,double? x=null,double? y=null,double? z=null,double? lower=null,double? upper=null,
                double? open=null,double? high=null,double? low=null,double? close=null,double? volume=null,double? value=null,
                string? label=null,string? color=null,int? v0=null,int? v1=null,int? v2=null)
            {
                writer.Write(Text(s.Id)); writer.Write(','); writer.Write(Text(s.Title)); writer.Write(','); writer.Write(kind); writer.Write(','); writer.Write(index.ToString(CultureInfo.InvariantCulture));
                foreach(var number in new[]{x,y,z,lower,upper,open,high,low,close,volume,value})
                { writer.Write(','); if(number is { } n&&!double.IsNaN(n)) writer.Write(n.ToString("R",CultureInfo.InvariantCulture)); }
                writer.Write(','); writer.Write(Text(label)); writer.Write(','); writer.Write(Text(color??s.Color.ToHex()));
                foreach(var vertex in new[]{v0,v1,v2}) { writer.Write(','); if(vertex is { } n) writer.Write(n.ToString(CultureInfo.InvariantCulture)); }
                writer.Write("\r\n");
            }
            switch(s)
            {
                case DataSeries data:
                    for(var i=0;i<data.Data.Count;i++)
                    {
                        var x=data.Data.X(i); var y=data.Data.Y(i); double? lower=null,upper=null;
                        if(data is BandSeries band) { lower=y; upper=band.Upper.Y(i); }
                        if(data is ErrorBarSeries error) { lower=y-error.LowerErrors.Span[i]; upper=y+error.UpperErrors.Span[i]; }
                        Row(data is BarSeries?"bar":data is BandSeries?"band":data is ErrorBarSeries?"error":"line",i,x,y,lower:lower,upper:upper);
                    }
                    break;
                case FinancialSeries financial:
                    for(var i=0;i<financial.Points.Count;i++)
                    {
                        var p=financial.Points[i]; Row("financial",i,p.X,open:p.Open,high:p.High,low:p.Low,close:p.Close,volume:p.Volume);
                    }
                    break;
                case HeatmapSeries heat:
                    var g=heat.Grid;
                    for(var row=0;row<g.Rows;row++) for(var col=0;col<g.Columns;col++)
                        Row("grid",row*g.Columns+col,g.X(col),g.Y(row),value:g[col,row],color:heat.ResolveColorScale().Color(g[col,row]).ToHex());
                    break;
                case PointLineSeries3D spatial:
                    for(var i=0;i<spatial.Points.Length;i++) { var p=spatial.Points.Span[i]; Row("point3d",i,p.X,p.Y,p.Z); }
                    break;
                case MeshSeries3D mesh:
                    for(var i=0;i<mesh.Mesh.Vertices.Length;i++)
                    { var v=mesh.Mesh.Vertices.Span[i]; Row("mesh-vertex",i,v.Position.X,v.Position.Y,v.Position.Z,color:v.Color.ToHex()); }
                    for(var i=0;i<mesh.Mesh.Indices.Length;i+=3)
                        Row("mesh-triangle",i/3,v0:mesh.Mesh.Indices.Span[i],v1:mesh.Mesh.Indices.Span[i+1],v2:mesh.Mesh.Indices.Span[i+2]);
                    break;
                case PieSeries pie:
                    for(var i=0;i<pie.Slices.Length;i++) { var slice=pie.Slices.Span[i]; Row("pie",i,value:slice.Value,label:slice.Label,color:slice.Color.ToHex()); }
                    break;
                default:throw new NotSupportedException("CSV export is not defined for "+s.GetType().Name);
            }
        }
    }
    private static string Text(string? value)
    {
        if(string.IsNullOrEmpty(value)) return "";
        var first=value.TrimStart();
        if(first.Length>0&&first[0] is '=' or '+' or '-' or '@'||value[0] is '\t' or '\r') value="'"+value;
        return "\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    }
}
