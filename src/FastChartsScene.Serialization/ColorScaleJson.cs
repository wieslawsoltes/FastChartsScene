using System.Text.Json;
using FastChartsScene.Charts;
using FastChartsScene.Core;

namespace FastChartsScene.Serialization;

public static partial class ChartJson
{
    // Additive version-1 fields. Older documents retain the original heat palette and hidden colorbar.
    private static void WriteColorSettings(Utf8JsonWriter writer, HeatmapSeries series)
    {
        var scale = series.ColorScale;
        writer.WriteStartObject("colorScale");
        writer.WriteString("mode", scale.Mode.ToString()); writer.WriteBoolean("reversed", scale.Reversed);
        writer.WriteString("outOfRange", scale.OutOfRange.ToString()); Color(writer, "missingColor", scale.MissingColor);
        if (scale.Range is { } range) { writer.WriteStartArray("range"); writer.WriteNumberValue(range.Minimum); writer.WriteNumberValue(range.Maximum); writer.WriteEndArray(); }
        else writer.WriteNull("range");
        writer.WriteStartObject("palette"); writer.WriteString("name", scale.Palette.Name);
        writer.WriteString("interpolation", scale.Palette.Interpolation.ToString()); writer.WriteStartArray("stops");
        foreach (var stop in scale.Palette.Stops)
        { writer.WriteStartObject(); writer.WriteNumber("offset", stop.Offset); Color(writer, "color", stop.Color); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
        writer.WriteStartObject("colorBar"); writer.WriteBoolean("visible", series.ShowColorBar);
        writer.WriteString("title", series.ValueTitle); writer.WriteString("units", series.ValueUnits); writer.WriteEndObject();
    }
    private sealed partial class Reader
    {
        private void ReadColorSettings(JsonElement element, HeatmapSeries series)
        {
            if (element.TryGetProperty("colorScale", out var scale))
            {
                var palette = scale.GetProperty("palette");
                var stops = Items(palette.GetProperty("stops"), ColorMap.MaximumStops)
                    .Select(stop => new ColorStop(Number(stop, "offset"), Color(stop, "color"))).ToArray();
                var map = new ColorMap(Text(palette, "name"), stops, EnumValue<ColorInterpolation>(palette, "interpolation"));
                var rangeElement = scale.GetProperty("range"); AxisRange? range = null;
                if (rangeElement.ValueKind != JsonValueKind.Null) { var tuple = Tuple(rangeElement, 2); range = new(tuple[0], tuple[1]); }
                series.ColorScale = new(map, range, EnumValue<ColorScaleMode>(scale, "mode"), scale.GetProperty("reversed").GetBoolean(),
                    Color(scale, "missingColor"), EnumValue<ColorOutOfRange>(scale, "outOfRange"));
            }
            if (element.TryGetProperty("colorBar", out var bar))
            {
                series.ShowColorBar = bar.GetProperty("visible").GetBoolean();
                series.ValueTitle = Text(bar, "title"); series.ValueUnits = Text(bar, "units");
            }
        }
    }
}
