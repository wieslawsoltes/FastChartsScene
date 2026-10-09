using System.Globalization;

namespace FastChartsScene.Core;

public enum AxisScale { Linear, Logarithmic, DateTime, Category }
public enum AxisSide { Near, Far }

public readonly record struct AxisRange
{
    public double Minimum { get; }
    public double Maximum { get; }
    public double Span => Maximum-Minimum;
    public AxisRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum || !double.IsFinite(maximum-minimum))
            throw new ArgumentOutOfRangeException(nameof(maximum), "An axis needs a finite, positive span.");
        Minimum=minimum; Maximum=maximum;
    }
    public static AxisRange FromData(double min, double max, double padding=0.05)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max)) return new(0,1);
        if (max < min) (min,max)=(max,min);
        if (max == min) { var d=Math.Max(1,Math.Abs(min)*0.05); min-=d; max+=d; }
        var p=(max-min)*Math.Clamp(padding,0,1);
        return new(min-p,max+p);
    }
}

public readonly record struct AxisTick(double Value, double Unit, string Label);

public sealed class Axis : ObservableModel
{
    private string _title="";
    private string _units="";
    private AxisRange _range=new(0,10);
    private AxisScale _scale;
    private bool _inverted;
    private AxisSide _side;
    private double _weight=1;
    public string Id { get; }
    public Axis(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); Id=id; }
    public string Title { get=>_title; set=>Set(ref _title,value??""); }
    public string Units { get=>_units; set=>Set(ref _units,value??""); }
    public AxisRange Range { get=>_range; set { Validate(value,_scale); Set(ref _range,value); } }
    public AxisScale Scale { get=>_scale; set { Validate(_range,value); Set(ref _scale,value); } }
    public bool Inverted { get=>_inverted; set=>Set(ref _inverted,value); }
    public AxisSide Side { get=>_side; set=>Set(ref _side,value); }
    public double Weight { get=>_weight; set { if (!double.IsFinite(value)||value<=0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _weight,value); } }
    public string[] Categories { get; set; }=[];
    private static void Validate(AxisRange r, AxisScale s)
    {
        if (!(r.Span>0) || !double.IsFinite(r.Span)) throw new ArgumentException("Invalid range.");
        if (s==AxisScale.Logarithmic && r.Minimum<=0) throw new ArgumentOutOfRangeException(nameof(r),"Logarithmic ranges must be positive.");
    }
    public double ToUnit(double value)
    {
        var a=Range.Minimum; var b=Range.Maximum;
        if (Scale==AxisScale.Logarithmic) { if (value<=0) return double.NaN; value=Math.Log10(value); a=Math.Log10(a); b=Math.Log10(b); }
        var u=(value-a)/(b-a);
        return Inverted?1-u:u;
    }
    public double FromUnit(double unit)
    {
        if (Inverted) unit=1-unit;
        if (Scale==AxisScale.Logarithmic) return Math.Pow(10,Math.Log10(Range.Minimum)+unit*(Math.Log10(Range.Maximum)-Math.Log10(Range.Minimum)));
        return Range.Minimum+unit*Range.Span;
    }
    public void Zoom(double anchorUnit,double factor)
    {
        if (!double.IsFinite(anchorUnit)||!double.IsFinite(factor)||factor<=0) throw new ArgumentOutOfRangeException(nameof(factor));
        SetFromUnits(anchorUnit+(0-anchorUnit)*factor,anchorUnit+(1-anchorUnit)*factor);
    }
    public void Pan(double deltaUnit) => SetFromUnits(deltaUnit,1+deltaUnit);
    public void SetFromUnits(double a,double b)
    {
        var min=FromUnit(a); var max=FromUnit(b);
        Range=new(Math.Min(min,max),Math.Max(min,max));
    }
    public IReadOnlyList<AxisTick> Ticks(int target=7)
    {
        target=Math.Clamp(target,2,40);
        List<AxisTick> ticks=[];
        if (Scale==AxisScale.Logarithmic)
        {
            var start=(int)Math.Floor(Math.Log10(Range.Minimum)); var end=(int)Math.Ceiling(Math.Log10(Range.Maximum));
            var stride=Math.Max(1,(end-start)/target);
            for (var e=start;e<=end && ticks.Count<100;e+=stride)
            {
                var v=Math.Pow(10,e);
                if (v>=Range.Minimum*(1-1e-12)&&v<=Range.Maximum*(1+1e-12)) ticks.Add(new(v,ToUnit(v),Format(v)));
            }
            if (ticks.Count>=2) return ticks;
        }
        var step=NiceStep(Range.Span/(target-1));
        if (Scale==AxisScale.Category) step=Math.Max(1,Math.Ceiling(step));
        var first=Math.Ceiling(Range.Minimum/step-1e-12)*step;
        for(var i=0;i<100;i++)
        {
            var v=first+i*step;
            if(v>Range.Maximum+step*1e-8) break;
            if(v<Range.Minimum-step*1e-8) continue;
            if(Math.Abs(v)<step*1e-10) v=0;
            ticks.Add(new(v,ToUnit(v),Format(v,step)));
        }
        return ticks;
    }
    public string Format(double value,double step=0)
    {
        if(Scale==AxisScale.Category)
        {
            var i=(int)Math.Round(value); return i>=0&&i<Categories.Length?Categories[i]:i.ToString(CultureInfo.InvariantCulture);
        }
        if(Scale==AxisScale.DateTime)
        {
            try { return DateTimeOffset.UnixEpoch.AddSeconds(value).ToString(Range.Span>86400?"MMM dd":Range.Span>120?"HH:mm:ss":"HH:mm:ss.fff",CultureInfo.InvariantCulture); }
            catch(ArgumentOutOfRangeException) { return "out of date range"; }
        }
        var a=Math.Abs(value);
        if(a!=0&&(a>=1e6||a<1e-4)) return value.ToString("0.###E+0",CultureInfo.InvariantCulture);
        var decimals=step>0?Math.Clamp((int)Math.Ceiling(-Math.Log10(step))+1,0,9):4;
        return value.ToString(decimals==0?"0":"0."+new string('#',decimals),CultureInfo.InvariantCulture);
    }
    public static double NiceStep(double raw)
    {
        if(!double.IsFinite(raw)||raw<=0) throw new ArgumentOutOfRangeException(nameof(raw));
        var p=Math.Pow(10,Math.Floor(Math.Log10(raw))); var m=raw/p;
        return (m<=1?1:m<=2?2:m<=2.5?2.5:m<=5?5:10)*p;
    }
}
