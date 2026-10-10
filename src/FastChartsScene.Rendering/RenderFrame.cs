using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FastChartsScene.Core;
using FastChartsScene.Charts;

namespace FastChartsScene.Rendering;

public enum PrimitiveKind { Triangles, Points }
public sealed record DrawBatch(int Start, int Count, PrimitiveKind Kind, bool DepthTest,
    RectD Clip, float[] Transform, float PointSize = 1);
public sealed record TextLabel(string Text, double X, double Y, string Color, double Size = 12,
    string Align = "left", double Rotation = 0, bool Bold = false);
public sealed record HitRegion(string Kind, string Id, RectD Bounds);
public sealed record FrameMetadata(double Width, double Height, string Background,
    DrawBatch[] Batches, TextLabel[] Labels, HitRegion[] Hits, RectD Plot, long Revision,
    long SourcePoints, int VertexCount, string Title);

/// <summary>Backend-neutral packed geometry. Each vertex is XYZ, RGBA and normal (ten floats).
/// A frame owns its buffers; consumers must not mutate them. Matrices use System.Numerics row-vector convention.</summary>
public sealed class RenderFrame(float[] vertices, FrameMetadata metadata, ChartLayout layout)
{
    public const int VertexStride = 10;
    public float[] Vertices { get; } = vertices;
    public FrameMetadata Metadata { get; } = metadata;
    public ChartLayout Layout { get; } = layout;
    public string MetadataJson() => JsonSerializer.Serialize(Metadata, RenderingJsonContext.Default.FrameMetadata);
    public void Validate()
    {
        if (Vertices.Length % VertexStride != 0 || Metadata.VertexCount != Vertices.Length / VertexStride)
            throw new InvalidOperationException("Invalid vertex buffer length.");
        if (Vertices.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Non-finite vertex.");
        foreach (var batch in Metadata.Batches)
        {
            if (batch.Start < 0 || batch.Count < 0 || (long)batch.Start + batch.Count > Metadata.VertexCount ||
                batch.Transform.Length != 16 || batch.Transform.Any(v => !float.IsFinite(v)) ||
                (batch.Kind == PrimitiveKind.Triangles && batch.Count % 3 != 0))
                throw new InvalidOperationException("Invalid draw batch.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FrameMetadata))]
internal partial class RenderingJsonContext : JsonSerializerContext { }

public sealed class ChartLayout
{
    public RectD Plot { get; }
    public IReadOnlyDictionary<string, RectD> AxisPlots { get; }
    private ChartLayout(RectD plot, Dictionary<string, RectD> axisPlots)
    { Plot = plot; AxisPlots = new System.Collections.ObjectModel.ReadOnlyDictionary<string, RectD>(axisPlots); }
    public RectD ForAxis(string id) => AxisPlots.TryGetValue(id, out var p) ? p : Plot;
    public static ChartLayout Create(ChartDocument document, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        var errors = document.Validate();
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
        var layered = document.AxisLayout == AxisLayout.Layered && document.View == ChartView.XY;
        var near = layered ? Math.Max(1, document.YAxes.Count(a => a.Side == AxisSide.Near)) : 1;
        var far = layered ? document.YAxes.Count(a => a.Side == AxisSide.Far) : 0;
        var left = 72 + Math.Max(0, near - 1) * 52;
        var right = 24 + far * 52;
        var top = document.Subtitle.Length == 0 ? 55 : 74;
        var bottom = 54 + Math.Max(0, document.XAxes.Count - 1) * 36;
        if (document.View is ChartView.Polar or ChartView.Smith) { left = 48; right = 48; bottom = 38; }
        if (document.View is ChartView.ThreeD or ChartView.Pie3D) { left = 28; right = 28; bottom = 28; }
        var actualLeft = Math.Min(left, width * .35);
        var actualRight = Math.Min(right, width * .2);
        var actualTop = Math.Min(top, height * .25);
        var actualBottom = Math.Min(bottom, height * .25);
        var plot = new RectD(actualLeft, actualTop, width - actualLeft - actualRight, height - actualTop - actualBottom);
        Dictionary<string, RectD> mapping = [];
        if (document.AxisLayout == AxisLayout.Stacked && document.View == ChartView.XY)
        {
            var weights = document.YAxes.Select(a => a.Weight).ToArray();
            var max = weights.Max(); var total = weights.Sum(w => w / max);
            var gap = Math.Min(12, plot.Height / (document.YAxes.Count * 4));
            var available = plot.Height - gap * (document.YAxes.Count - 1); var y = plot.Y;
            foreach (var a in document.YAxes)
            {
                var h = available * (a.Weight / max) / total;
                mapping[a.Id] = new(plot.X, y, plot.Width, h); y += h + gap;
            }
        }
        else if (document.AxisLayout == AxisLayout.Segmented && document.View == ChartView.XY)
        {
            var max = document.Segments.Max(s => s.Weight); var total = document.Segments.Sum(s => s.Weight / max);
            var gap = Math.Min(12, plot.Height / (document.Segments.Count * 4));
            var available = plot.Height - gap * (document.Segments.Count - 1); var y = plot.Y;
            foreach (var s in document.Segments)
            {
                var h = available * (s.Weight / max) / total;
                foreach (var id in s.AxisIds) mapping[id] = new(plot.X, y, plot.Width, h);
                y += h + gap;
            }
        }
        foreach (var a in document.YAxes) mapping.TryAdd(a.Id, plot);
        return new(plot, mapping);
    }
    public PointD ToScreen(Axis x, Axis y, PointD point)
    {
        var p = ForAxis(y.Id);
        return new(p.X + x.ToUnit(point.X) * p.Width, p.Bottom - y.ToUnit(point.Y) * p.Height);
    }
    public PointD FromScreen(Axis x, Axis y, PointD point)
    {
        var p = ForAxis(y.Id);
        return new(x.FromUnit((point.X - p.X) / p.Width), y.FromUnit((p.Bottom - point.Y) / p.Height));
    }
}

internal sealed class FrameBuilder(double width, double height)
{
    public readonly List<float> Vertices = [];
    public readonly List<DrawBatch> Batches = [];
    public readonly List<TextLabel> Labels = [];
    public readonly List<HitRegion> Hits = [];
    public double Width { get; } = width;
    public double Height { get; } = height;
    public int Count => Vertices.Count / RenderFrame.VertexStride;
    public static float[] MatrixArray(Matrix4x4 m) =>
        [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    public void Batch(int start, RectD clip, bool depth = false, Matrix4x4? matrix = null,
        PrimitiveKind kind = PrimitiveKind.Triangles, float pointSize = 1)
    {
        if (Count > start) Batches.Add(new(start, Count - start, kind, depth, clip,
            MatrixArray(matrix ?? Matrix4x4.Identity), pointSize));
    }
    public void Vertex(Vector3 p, ColorRgba c, Vector3 normal = default)
    {
        Vertices.Add(p.X); Vertices.Add(p.Y); Vertices.Add(p.Z);
        Vertices.Add(c.R); Vertices.Add(c.G); Vertices.Add(c.B); Vertices.Add(c.A);
        Vertices.Add(normal.X); Vertices.Add(normal.Y); Vertices.Add(normal.Z);
    }
    public Vector3 Screen(PointD p, float depth = 0) => new((float)(2 * p.X / Width - 1), (float)(1 - 2 * p.Y / Height), depth);
    public void Triangle(PointD a, PointD b, PointD c, ColorRgba color)
    {
        if (!a.IsFinite || !b.IsFinite || !c.IsFinite) return;
        Vertex(Screen(a), color); Vertex(Screen(b), color); Vertex(Screen(c), color);
    }
    public void Triangle3D(Vector3 a, Vector3 b, Vector3 c, ColorRgba ca, ColorRgba cb, ColorRgba cc, bool lit = true)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var n = lit && cross.LengthSquared() > 1e-16f ? Vector3.Normalize(cross) : Vector3.Zero;
        Vertex(a, ca, n); Vertex(b, cb, n); Vertex(c, cc, n);
    }
    public void Rect(RectD p, ColorRgba color)
    {
        if (p.Width <= 0 || p.Height <= 0) return;
        Triangle(new(p.X,p.Y),new(p.Right,p.Y),new(p.Right,p.Bottom),color);
        Triangle(new(p.X,p.Y),new(p.Right,p.Bottom),new(p.X,p.Bottom),color);
    }
    public void Line(PointD a, PointD b, ColorRgba color, double thickness, RectD clip)
    {
        if (thickness <= 0 || !GeometryAlgorithms.ClipLine(clip, ref a, ref b)) return;
        var dx=b.X-a.X; var dy=b.Y-a.Y; var length=Math.Sqrt(dx*dx+dy*dy);
        if (length < 1e-9) return;
        var nx=-dy/length*thickness/2; var ny=dx/length*thickness/2;
        var p=new PointD(a.X+nx,a.Y+ny); var q=new PointD(a.X-nx,a.Y-ny);
        var r=new PointD(b.X-nx,b.Y-ny); var s=new PointD(b.X+nx,b.Y+ny);
        Triangle(p,q,r,color); Triangle(p,r,s,color);
    }
    public void Marker(PointD p, MarkerShape shape, double size, ColorRgba color, RectD clip)
    {
        if (!p.IsFinite || !clip.Contains(p) || shape == MarkerShape.None || size <= 0) return;
        var r=size/2;
        if (shape == MarkerShape.Square) { Rect(new(p.X-r,p.Y-r,size,size),color); return; }
        if (shape == MarkerShape.Cross)
        {
            Line(new(p.X-r,p.Y-r),new(p.X+r,p.Y+r),color,1.5,clip);
            Line(new(p.X+r,p.Y-r),new(p.X-r,p.Y+r),color,1.5,clip); return;
        }
        var count=shape == MarkerShape.Circle ? 16 : shape == MarkerShape.Triangle ? 3 : 4;
        for (var i=0; i<count; i++)
        {
            var a=-Math.PI/2+i*2*Math.PI/count; var b=-Math.PI/2+(i+1)*2*Math.PI/count;
            Triangle(p,new(p.X+Math.Cos(a)*r,p.Y+Math.Sin(a)*r),new(p.X+Math.Cos(b)*r,p.Y+Math.Sin(b)*r),color);
        }
    }
    public void Text(string text, double x, double y, ColorRgba color, double size=12,
        string align="left", double rotation=0, bool bold=false) =>
        Labels.Add(new(text,x,y,color.ToHex(),size,align,rotation,bold));
}
