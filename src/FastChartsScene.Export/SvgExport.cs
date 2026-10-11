using System.Globalization;
using System.Text;
using System.Xml;
using FastChartsScene.Core;
using FastChartsScene.Rendering;

namespace FastChartsScene.Export;

/// <summary>Vector export of the actual clipped 2D frame, including text, themes and transparency.
/// Depth-tested scenes are rejected; a painter's approximation would not preserve their visibility.</summary>
public static class SvgExport
{
    private const string Namespace="http://www.w3.org/2000/svg";
    public static string Write(RenderFrame frame)
    {
        var text=new StringBuilder(); using(var writer=new StringWriter(text,CultureInfo.InvariantCulture)) Write(frame,writer); return text.ToString();
    }
    public static void Write(RenderFrame frame,TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(output); frame.Validate();
        if(frame.Metadata.Batches.Any(b=>b.DepthTest||b.Kind!=PrimitiveKind.Triangles))
            throw new NotSupportedException("SVG export requires a 2D triangle frame. Use raster export for 3D views.");
        using var xml=XmlWriter.Create(output,new XmlWriterSettings { OmitXmlDeclaration=true,CloseOutput=false,CheckCharacters=true });
        void Element(string name)=>xml.WriteStartElement(name,Namespace);
        void Attribute(string name,string value)=>xml.WriteAttributeString(name,value);
        void Number(string name,double value)=>Attribute(name,value.ToString("R",CultureInfo.InvariantCulture));
        void Color(ColorRgba color)
        {
            Attribute("fill",color.ToHex()[..7]); if(color.A!=1) Number("fill-opacity",color.A);
        }
        void Rect(RectD rect)
        { Number("x",rect.X); Number("y",rect.Y); Number("width",rect.Width); Number("height",rect.Height); }
        Element("svg"); Number("width",frame.Metadata.Width); Number("height",frame.Metadata.Height);
        Attribute("viewBox",FormattableString.Invariant($"0 0 {frame.Metadata.Width:R} {frame.Metadata.Height:R}")); Attribute("role","img");
        Element("title"); xml.WriteString(frame.Metadata.Title); xml.WriteEndElement();
        Element("rect"); Rect(new(0,0,frame.Metadata.Width,frame.Metadata.Height)); Color(ColorRgba.Parse(frame.Metadata.Background)); xml.WriteEndElement();
        Element("defs");
        for(var i=0;i<frame.Metadata.Batches.Length;i++)
        {
            Element("clipPath"); Attribute("id","clip"+i); Element("rect"); Rect(frame.Metadata.Batches[i].Clip); xml.WriteEndElement(); xml.WriteEndElement();
        }
        xml.WriteEndElement();
        for(var batchIndex=0;batchIndex<frame.Metadata.Batches.Length;batchIndex++)
        {
            var batch=frame.Metadata.Batches[batchIndex]; var transform=batch.Transform;
            Element("g"); Attribute("clip-path",$"url(#clip{batchIndex})");
            for(var vertex=batch.Start;vertex<batch.Start+batch.Count;vertex+=3)
            {
                var points=new StringBuilder(); ColorRgba? firstColor=null;
                for(var corner=0;corner<3;corner++)
                {
                    var at=(vertex+corner)*RenderFrame.VertexStride; var v=frame.Vertices;
                    var color=new ColorRgba(v[at+3],v[at+4],v[at+5],v[at+6]);
                    if(firstColor!=null&&firstColor.Value!=color) throw new NotSupportedException("Interpolated per-vertex colors require raster export.");
                    firstColor=color;
                    var x=v[at]*transform[0]+v[at+1]*transform[4]+v[at+2]*transform[8]+transform[12];
                    var y=v[at]*transform[1]+v[at+1]*transform[5]+v[at+2]*transform[9]+transform[13];
                    var w=v[at]*transform[3]+v[at+1]*transform[7]+v[at+2]*transform[11]+transform[15];
                    if(!double.IsFinite(w)||w<=0) throw new NotSupportedException("Perspective clipping requires raster export.");
                    if(corner>0) points.Append(' ');
                    points.Append(((x/w+1)*frame.Metadata.Width/2).ToString("R",CultureInfo.InvariantCulture)); points.Append(',');
                    points.Append(((1-y/w)*frame.Metadata.Height/2).ToString("R",CultureInfo.InvariantCulture));
                }
                Element("polygon"); Attribute("points",points.ToString()); Color(firstColor!.Value); xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        foreach(var label in frame.Metadata.Labels)
        {
            Element("text"); Number("x",label.X); Number("y",label.Y); Number("font-size",label.Size);
            Attribute("font-family","sans-serif"); Attribute("text-anchor",label.Align=="center"?"middle":label.Align=="right"?"end":"start");
            if(label.Bold) Attribute("font-weight","bold");
            if(label.Rotation!=0) Attribute("transform",FormattableString.Invariant($"rotate({label.Rotation:R} {label.X:R} {label.Y:R})"));
            Color(ColorRgba.Parse(label.Color)); xml.WriteString(label.Text); xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }
}
