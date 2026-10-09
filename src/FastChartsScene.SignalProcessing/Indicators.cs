namespace FastChartsScene.SignalProcessing;

public static class Indicators
{
    private static void Validate(ReadOnlySpan<double> values,int period)
    {
        if(period<1) throw new ArgumentOutOfRangeException(nameof(period));
        foreach(var v in values) if(!double.IsFinite(v)) throw new ArgumentException("Indicator inputs must be finite.");
    }
    public static double[] Sma(ReadOnlySpan<double> values,int period)
    {
        Validate(values,period); var result=Enumerable.Repeat(double.NaN,values.Length).ToArray(); var sum=0.0;
        for(var i=0;i<values.Length;i++) { sum+=values[i]; if(i>=period) sum-=values[i-period]; if(i>=period-1) result[i]=sum/period; }
        return result;
    }
    public static double[] Ema(ReadOnlySpan<double> values,int period)
    {
        Validate(values,period); var result=Enumerable.Repeat(double.NaN,values.Length).ToArray();
        if(values.Length<period) return result;
        var sum=0.0; for(var i=0;i<period;i++) sum+=values[i]; var ema=sum/period; result[period-1]=ema; var alpha=2.0/(period+1);
        for(var i=period;i<values.Length;i++) result[i]=ema+=alpha*(values[i]-ema);
        return result;
    }
    public static double[] Rsi(ReadOnlySpan<double> values,int period=14)
    {
        Validate(values,period); var result=Enumerable.Repeat(double.NaN,values.Length).ToArray();
        if(values.Length<=period) return result;
        var gain=0.0; var loss=0.0;
        for(var i=1;i<=period;i++) { var d=values[i]-values[i-1]; gain+=Math.Max(d,0); loss+=Math.Max(-d,0); }
        gain/=period; loss/=period;
        static double Value(double g,double l)=>g==0&&l==0?50:l==0?100:100-100/(1+g/l);
        result[period]=Value(gain,loss);
        for(var i=period+1;i<values.Length;i++) { var d=values[i]-values[i-1]; gain=(gain*(period-1)+Math.Max(d,0))/period; loss=(loss*(period-1)+Math.Max(-d,0))/period; result[i]=Value(gain,loss); }
        return result;
    }
    public static (double[] Middle,double[] Upper,double[] Lower) Bollinger(ReadOnlySpan<double> values,int period=20,double deviations=2)
    {
        Validate(values,period); if(!double.IsFinite(deviations)||deviations<0) throw new ArgumentOutOfRangeException(nameof(deviations));
        var mid=Sma(values,period); var upper=Enumerable.Repeat(double.NaN,values.Length).ToArray(); var lower=(double[])upper.Clone();
        for(var i=period-1;i<values.Length;i++)
        {
            var variance=0.0; for(var j=i-period+1;j<=i;j++) { var d=values[j]-mid[i]; variance+=d*d; }
            var sigma=Math.Sqrt(variance/period)*deviations; upper[i]=mid[i]+sigma; lower[i]=mid[i]-sigma;
        }
        return(mid,upper,lower);
    }
}
