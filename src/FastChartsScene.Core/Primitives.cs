using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FastChartsScene.Core;

public readonly record struct PointD(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}

public readonly record struct ColorRgba(float R, float G, float B, float A = 1)
{
    public static ColorRgba Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        hex = hex.TrimStart('#');
        if (hex.Length is not (6 or 8)) throw new FormatException("Expected RRGGBB or RRGGBBAA.");
        var n = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 6
            ? new((n >> 16 & 255) / 255f, (n >> 8 & 255) / 255f, (n & 255) / 255f)
            : new((n >> 24 & 255) / 255f, (n >> 16 & 255) / 255f, (n >> 8 & 255) / 255f, (n & 255) / 255f);
    }
    public ColorRgba WithAlpha(float alpha) => this with { A = Math.Clamp(alpha, 0, 1) };
    public static ColorRgba Lerp(ColorRgba a, ColorRgba b, double t)
    {
        var f = (float)Math.Clamp(t, 0, 1);
        return new(a.R + (b.R-a.R)*f, a.G + (b.G-a.G)*f, a.B + (b.B-a.B)*f, a.A + (b.A-a.A)*f);
    }
    public string ToHex() => $"#{(byte)Math.Clamp(Math.Round(R*255),0,255):X2}{(byte)Math.Clamp(Math.Round(G*255),0,255):X2}{(byte)Math.Clamp(Math.Round(B*255),0,255):X2}{(byte)Math.Clamp(Math.Round(A*255),0,255):X2}";
}

/// <summary>UI-thread-affine observable model. Data producers use the thread-safe rolling buffer.</summary>
public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
        return true;
    }
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class Palette
{
    public static readonly ColorRgba[] Series = [ColorRgba.Parse("E9D53E"), ColorRgba.Parse("17B5E8"), ColorRgba.Parse("EC665B"), ColorRgba.Parse("72CE6F"), ColorRgba.Parse("C688E8"), ColorRgba.Parse("FFAA52")];
    private static readonly ColorRgba[] HeatStops = [ColorRgba.Parse("09134D"),ColorRgba.Parse("164BC4"),ColorRgba.Parse("04BCD0"),ColorRgba.Parse("55D574"),ColorRgba.Parse("F6ED4C"),ColorRgba.Parse("F68B30"),ColorRgba.Parse("BA1740")];
    public static ColorRgba Heat(double t)
    {
        if (!double.IsFinite(t)) return new(0,0,0,0);
        var p = Math.Clamp(t, 0, 1) * (HeatStops.Length-1);
        var i = Math.Min((int)p, HeatStops.Length-2);
        return ColorRgba.Lerp(HeatStops[i], HeatStops[i+1], p-i);
    }
}

public sealed record ChartTheme(ColorRgba Background, ColorRgba Plot, ColorRgba Grid, ColorRgba Text, ColorRgba Accent)
{
    public static ChartTheme Dark { get; } = new(ColorRgba.Parse("252525"),ColorRgba.Parse("111315"),ColorRgba.Parse("34383B"),ColorRgba.Parse("CDD0D2"),ColorRgba.Parse("E2C323"));
    public static ChartTheme Light { get; } = new(ColorRgba.Parse("F4F4F4"),ColorRgba.Parse("FFFFFF"),ColorRgba.Parse("D9DEE2"),ColorRgba.Parse("30343A"),ColorRgba.Parse("9B6800"));
    public static ChartTheme HighContrast { get; } = new(ColorRgba.Parse("000000"),ColorRgba.Parse("000000"),ColorRgba.Parse("6A6A6A"),ColorRgba.Parse("FFFFFF"),ColorRgba.Parse("FFFF00"));
}
