using System.Runtime.CompilerServices;
using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Rendering;

public sealed partial class SceneCompiler
{
    private sealed record ContourCache(ScalarGrid Grid, ReadOnlyMemory<double> Levels, IReadOnlyList<ContourSegment> Segments);
    private readonly ConditionalWeakTable<ContourSeries, ContourCache> _contours = new();
    /// <summary>Number of contour topology builds, excluding projection/color-only updates.</summary>
    public long ContourGenerationCount { get; private set; }
    private void DrawContours(ContourSeries series, FrameBuilder frame, RectD clip, Func<PointD, PointD> map)
    {
        if (!_contours.TryGetValue(series, out var cached) || !ReferenceEquals(cached.Grid, series.Grid) || !cached.Levels.Equals(series.Levels))
        {
            var segments = GeometryAlgorithms.Contours(series.Grid, series.Levels.Span);
            cached = new(series.Grid, series.Levels, segments);
            _contours.Remove(series); _contours.Add(series, cached); ContourGenerationCount++;
        }
        var scale = series.ResolveColorScale();
        foreach (var segment in cached.Segments)
            frame.Line(map(segment.A), map(segment.B), scale.Color(segment.Level), series.StrokeWidth, clip);
    }
}
