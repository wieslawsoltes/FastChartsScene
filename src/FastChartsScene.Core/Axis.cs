using System.Globalization;

namespace FastChartsScene.Core;

public enum AxisScale { Linear, Logarithmic, DateTime, Category }
public enum AxisSide { Near, Far }

public readonly record struct AxisRange
{
    public double Minimum { get; }
    public double Maximum { get; }
    public double Span=>Maximum-Minimum;
    public AxisRange(double minimum,double maximum)
    {
        if(!double.IsFinite(minimum)||!double.IsFinite(maximum)||maximum<=minimum||!double.IsFinite(maximum-minimum))
            throw new ArgumentOutOfRangeException(nameof(maximum),"An axis needs a finite, positive span.");
        Minimum=minimum; Maximum=maximum;
    }
    public static AxisRange FromData(double min,double max,double padding=.05)
    {
        if(!double.IsFinite(padding)) throw new ArgumentOutOfRangeException(nameof(padding));
        if(!double.IsFinite(min)||!double.IsFinite(max)) return new(0,1);
        if(max<min) (min,max)=(max,min);
        if(max==min)
        {
            var delta=Math.Max(1,Math.Abs(min)*.05);
            min=Math.Clamp(min-delta,-double.MaxValue,double.MaxValue); max=Math.Clamp(max+delta,-double.MaxValue,double.MaxValue);
        }
        var span=max-min;
        if(!double.IsFinite(span)) throw new ArgumentOutOfRangeException(nameof(max),"Data span exceeds the axis representation.");
        var p=span*Math.Clamp(padding,0,1);
        return new(Math.Clamp(min-p,-double.MaxValue,double.MaxValue),Math.Clamp(max+p,-double.MaxValue,double.MaxValue));
    }
}
public readonly record struct AxisTick(double Value,double Unit,string Label);

public sealed class Axis : ObservableModel
{
    private string _title="",_units="";
    private string[] _categories=[];
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
    public AxisScale Scale { get=>_scale; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Validate(_range,value); Set(ref _scale,value); } }
    public bool Inverted { get=>_inverted; set=>Set(ref _inverted,value); }
    public AxisSide Side { get=>_side; set { if(!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _side,value); } }
    public double Weight { get=>_weight; set { if(!double.IsFinite(value)||value<=0) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref _weight,value); } }
    public string[] Categories
    {
        get=>(string[])_categories.Clone();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if(value.Any(v=>v==null)) throw new ArgumentException("Category labels cannot be null.",nameof(value));
            if(_categories.SequenceEqual(value)) return; _categories=(string[])value.Clone(); Notify();
        }
    }
    private static void Validate(AxisRange r,AxisScale s)
    {
        if(!(r.Span>0)||!double.IsFinite(r.Span)) throw new ArgumentException("Invalid range.");
        if(s==AxisScale.Logarithmic&&r.Minimum<=0) throw new ArgumentOutOfRangeException(nameof(r),"Logarithmic ranges must be positive.");
    }
    private static double LogRatio(double value,double basis)
    {
        var relative=(value-basis)/basis;
        if(Math.Abs(relative)<1e-4)
            return relative*(1+relative*(-.5+relative*(1.0/3+relative*(-.25+relative*.2))));
        return double.IsFinite(relative)&&relative>-.5?Math.Log(1+relative):Math.Log(value)-Math.Log(basis);
    }
    private static double ExpMinusOne(double value)=>Math.Abs(value)<1e-4?
        value*(1+value*(.5+value*(1.0/6+value*(1.0/24+value/120)))):Math.Exp(value)-1;
    public double ToUnit(double value)
    {
        if(!double.IsFinite(value)) return double.NaN;
        var u=Scale==AxisScale.Logarithmic?(value<=0?double.NaN:LogRatio(value,Range.Minimum)/LogRatio(Range.Maximum,Range.Minimum)):
            (value-Range.Minimum)/Range.Span;
        return Inverted?1-u:u;
    }
    public double FromUnit(double unit)
    {
        if(Inverted) unit=1-unit;
        if(unit==0) return Range.Minimum; if(unit==1) return Range.Maximum;
        if(Scale!=AxisScale.Logarithmic) return Range.Minimum+unit*Range.Span;
        var step=unit*LogRatio(Range.Maximum,Range.Minimum);
        return Math.Abs(step)<.5?Range.Minimum+Range.Minimum*ExpMinusOne(step):Math.Exp(Math.Log(Range.Minimum)+step);
    }
    public void Zoom(double anchorUnit,double factor)
    {
        if(!double.IsFinite(anchorUnit)||!double.IsFinite(factor)||factor<=0) throw new ArgumentOutOfRangeException(nameof(factor));
        SetFromUnits(anchorUnit-anchorUnit*factor,anchorUnit+(1-anchorUnit)*factor);
    }
    public void Pan(double deltaUnit)
    { if(!double.IsFinite(deltaUnit)) throw new ArgumentOutOfRangeException(nameof(deltaUnit)); SetFromUnits(deltaUnit,1+deltaUnit); }
    public void SetFromUnits(double a,double b)
    { var min=FromUnit(a); var max=FromUnit(b); Range=new(Math.Min(min,max),Math.Max(min,max)); }
    public IReadOnlyList<AxisTick> Ticks(int target=7)
    {
        target=Math.Clamp(target,2,40); List<AxisTick> ticks=[];
        if(Scale==AxisScale.DateTime&&Range.Span>=45*86400.0&&TryCalendarTicks(target,ticks)) return ticks;
        if(Scale==AxisScale.Logarithmic)
        {
            var start=(int)Math.Floor(Math.Log10(Range.Minimum)); var end=(int)Math.Ceiling(Math.Log10(Range.Maximum));
            var stride=Math.Max(1,(end-start)/target);
            for(var e=start;e<=end&&ticks.Count<100;e+=stride)
            {
                var v=Math.Pow(10,e);
                if(v>0&&double.IsFinite(v)&&v>=Range.Minimum&&v<=Range.Maximum) ticks.Add(new(v,ToUnit(v),Format(v)));
            }
            if(ticks.Count>=2) return ticks; ticks.Clear();
        }
        var raw=Math.Max(double.Epsilon,Range.Span/(target-1));
        var step=Scale==AxisScale.DateTime?DateStep(raw):NiceStep(raw);
        if(Scale==AxisScale.Category) step=Math.Max(1,Math.Ceiling(step));
        var first=Math.Ceiling(Range.Minimum/step-1e-12)*step;
        for(var i=0;i<100;i++)
        {
            var v=first+i*step;
            if(!double.IsFinite(v)||v>Range.Maximum+step*1e-8) break;
            if(v<Range.Minimum-step*1e-8) continue;
            if(Math.Abs(v)<step*1e-10) v=0;
            if(ticks.Count>0&&v<=ticks[^1].Value) continue;
            ticks.Add(new(v,ToUnit(v),Format(v,step)));
        }
        return ticks;
    }
    private static double DateStep(double raw)
    {
        double[] steps=[.001,.002,.005,.01,.02,.05,.1,.2,.5,1,2,5,10,15,30,60,120,300,600,900,1800,3600,7200,10800,21600,43200,86400,172800,604800,1209600];
        foreach(var step in steps) if(step>=raw) return step;
        return NiceStep(raw);
    }
    private bool TryCalendarTicks(int target,List<AxisTick> ticks)
    {
        try
        {
            var start=DateTimeOffset.UnixEpoch.AddSeconds(Range.Minimum);
            var rawMonths=Range.Span/(target-1)/(86400*30.4375);
            var months=rawMonths<=1?1:rawMonths<=3?3:rawMonths<=6?6:rawMonths<=12?12:(int)Math.Min(120000,Math.Ceiling(rawMonths/12)*12);
            var absolute=(start.Year-1)*12+start.Month-1; var aligned=absolute/months*months;
            var tick=new DateTimeOffset(aligned/12+1,aligned%12+1,1,0,0,0,TimeSpan.Zero);
            for(var i=0;i<100;i++,tick=tick.AddMonths(months))
            {
                var value=(tick-DateTimeOffset.UnixEpoch).TotalSeconds;
                if(value>Range.Maximum) break;
                if(value>=Range.Minimum) ticks.Add(new(value,ToUnit(value),Format(value)));
            }
            return ticks.Count>0;
        }
        catch(ArgumentOutOfRangeException) { return ticks.Count>0; }
    }
    public string Format(double value,double step=0)
    {
        if(Scale==AxisScale.Category)
        {
            var rounded=Math.Round(value);
            return rounded>=0&&rounded<_categories.Length?_categories[(int)rounded]:rounded.ToString(CultureInfo.InvariantCulture);
        }
        if(Scale==AxisScale.DateTime)
        {
            var format=Range.Span>=730*86400.0?"yyyy":Range.Span>=90*86400.0?"MMM yyyy":Range.Span>86400?"MMM dd":Range.Span>120?"HH:mm:ss":"HH:mm:ss.fff";
            try { return DateTimeOffset.UnixEpoch.AddSeconds(value).ToString(format,CultureInfo.InvariantCulture); }
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
        var p=Math.Pow(10,Math.Floor(Math.Log10(raw))); if(p==0) return double.Epsilon;
        var m=raw/p; var result=(m<=1?1:m<=2?2:m<=2.5?2.5:m<=5?5:10)*p;
        return double.IsFinite(result)?result:double.MaxValue;
    }
}
