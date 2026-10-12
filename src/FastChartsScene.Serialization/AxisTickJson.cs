using System.Text.Json;
using FastChartsScene.Core;

namespace FastChartsScene.Serialization;

public static partial class ChartJson
{
    // Additive version-one settings. A legacy document without this object keeps automatic ticks.
    private static void WriteTickOptions(Utf8JsonWriter writer, Axis axis)
    {
        var options = axis.TickOptions;
        writer.WriteStartObject("tickOptions"); writer.WriteString("mode", options.Mode.ToString());
        writer.WriteString("numericFormat", options.NumericFormat); writer.WriteString("dateTimeFormat", options.DateTimeFormat);
        writer.WriteStartArray("ticks");
        foreach (var tick in options.CustomTicks)
        {
            writer.WriteStartObject(); writer.WriteNumber("value", tick.Value);
            if (tick.Label == null) writer.WriteNull("label"); else writer.WriteString("label", tick.Label);
            writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    private sealed partial class Reader
    {
        private void ReadTickOptions(JsonElement axisElement, Axis axis)
        {
            if (!axisElement.TryGetProperty("tickOptions", out var element)) return;
            var ticks = Items(element.GetProperty("ticks"), AxisTickOptions.MaximumTicks).Select(item =>
            {
                var value = Number(item, "value"); // Participate in the document's global numeric budget.
                var label = item.GetProperty("label");
                return new CustomAxisTick(value, label.ValueKind == JsonValueKind.Null ? null : label.GetString());
            }).ToArray();
            axis.TickOptions = new(EnumValue<AxisTickMode>(element, "mode"), ticks, Text(element, "numericFormat"), Text(element, "dateTimeFormat"));
        }
    }
}
