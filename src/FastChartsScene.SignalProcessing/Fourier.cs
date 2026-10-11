using System.Numerics;

namespace FastChartsScene.SignalProcessing;

public enum WindowFunction { Rectangular, Hann, Hamming, Blackman }
public sealed record SpectrumResult(double[] Frequencies,double[] Amplitudes);
/// <summary>One-sided peak-amplitude decibels relative to unit amplitude, floored at -240 dB.
/// Values are frequency-major: Values[frequency * TimeBins + time]. Time indices denote
/// window starts, with TimeStep = hop / sampleRate. FrequencyStep = sampleRate / windowLength.</summary>
public sealed record SpectrogramResult(int TimeBins,int FrequencyBins,double[] Values,double TimeStep,double FrequencyStep);

public static class Fourier
{
    /// <summary>In-place radix-2 Cooley-Tukey transform. Inverse includes 1/N normalization.</summary>
    public static void Transform(Span<Complex> values,bool inverse=false)
    {
        var n=values.Length;
        if(n<1||(n&(n-1))!=0) throw new ArgumentException("FFT length must be a positive power of two.");
        for(int i=1,j=0;i<n;i++)
        {
            var bit=n>>1;
            for(; (j&bit)!=0;bit>>=1) j^=bit;
            j^=bit; if(i<j) (values[i],values[j])=(values[j],values[i]);
        }
        for(var size=2;size<=n;size*=2)
        {
            var angle=(inverse?2:-2)*Math.PI/size; var root=Complex.FromPolarCoordinates(1,angle);
            for(var start=0;start<n;start+=size)
            {
                var w=Complex.One;
                for(var j=0;j<size/2;j++)
                {
                    var a=values[start+j]; var b=values[start+j+size/2]*w;
                    values[start+j]=a+b; values[start+j+size/2]=a-b; w*=root;
                }
            }
            if(size==n) break;
        }
        if(inverse) for(var i=0;i<n;i++) values[i]/=n;
    }
    public static double Window(WindowFunction window,int index,int count)
    {
        if(count<1||index<0||index>=count) throw new ArgumentOutOfRangeException(nameof(index));
        if(!Enum.IsDefined(window)) throw new ArgumentOutOfRangeException(nameof(window));
        return Weight(window,index,count);
    }
    private static double Weight(WindowFunction window,int index,int count)
    {
        if(count==1) return 1;
        var a=2*Math.PI*index/(count-1);
        return window switch { WindowFunction.Hann=>0.5-0.5*Math.Cos(a),WindowFunction.Hamming=>0.54-0.46*Math.Cos(a),WindowFunction.Blackman=>0.42-0.5*Math.Cos(a)+0.08*Math.Cos(2*a),_=>1 };
    }
    public static SpectrumResult Spectrum(ReadOnlySpan<double> samples,double sampleRate,WindowFunction window=WindowFunction.Hann)
    {
        if(!double.IsFinite(sampleRate)||sampleRate<=0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if(!Enum.IsDefined(window)) throw new ArgumentOutOfRangeException(nameof(window));
        var n=samples.Length;
        if(n<2||(n&(n-1))!=0) throw new ArgumentException("Spectrum needs a power-of-two sample count of at least two.");
        var values=new Complex[n]; var weight=0.0;
        for(var i=0;i<n;i++) { if(!double.IsFinite(samples[i])) throw new ArgumentException("FFT samples must be finite."); var w=Weight(window,i,n); values[i]=samples[i]*w; weight+=w; }
        if(weight<=0) throw new ArgumentException("Window has zero gain at this length.");
        Transform(values); var f=new double[n/2+1]; var a=new double[f.Length];
        for(var i=0;i<f.Length;i++) { f[i]=i*sampleRate/n; a[i]=values[i].Magnitude/weight*(i==0||i==n/2?1:2); }
        return new(f,a);
    }
    /// <summary>Hann-window STFT without per-window managed allocations. All input samples
    /// must be finite. The incomplete trailing window is discarded; result values are already dB.</summary>
    public static SpectrogramResult Spectrogram(ReadOnlySpan<double> samples,double sampleRate,int windowLength=256,int hop=64)
    {
        if(windowLength<4||(windowLength&(windowLength-1))!=0||hop<1||samples.Length<windowLength) throw new ArgumentOutOfRangeException(nameof(windowLength));
        if(!double.IsFinite(sampleRate)||sampleRate<=0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        foreach(var value in samples) if(!double.IsFinite(value)) throw new ArgumentException("STFT samples must be finite.",nameof(samples));
        var columns=1+(samples.Length-windowLength)/hop; var rows=windowLength/2+1;
        var data=new double[checked(columns*rows)];
        var weights=new double[windowLength]; var scratch=new Complex[windowLength]; var gain=0.0;
        for(var i=0;i<windowLength;i++) { weights[i]=Weight(WindowFunction.Hann,i,windowLength); gain+=weights[i]; }
        for(var col=0;col<columns;col++)
        {
            var offset=col*hop;
            for(var i=0;i<windowLength;i++) scratch[i]=samples[offset+i]*weights[i];
            Transform(scratch);
            for(var row=0;row<rows;row++)
            {
                var amplitude=scratch[row].Magnitude/gain*(row==0||row==rows-1?1:2);
                data[row*columns+col]=20*Math.Log10(Math.Max(amplitude,1e-12));
            }
        }
        return new(columns,rows,data,hop/sampleRate,sampleRate/windowLength);
    }
}

public sealed class BiquadLowPass
{
    private readonly double _b0,_b1,_b2,_a1,_a2;
    private double _z1,_z2;
    public BiquadLowPass(double sampleRate,double cutoff,double q=0.7071067811865476)
    {
        if(!double.IsFinite(sampleRate)||!double.IsFinite(cutoff)||!double.IsFinite(q)||sampleRate<=0||cutoff<=0||cutoff>=sampleRate/2||q<=0) throw new ArgumentOutOfRangeException(nameof(cutoff));
        var w=2*Math.PI*cutoff/sampleRate; var c=Math.Cos(w); var alpha=Math.Sin(w)/(2*q); var a0=1+alpha;
        _b0=(1-c)/2/a0; _b1=(1-c)/a0; _b2=_b0; _a1=-2*c/a0; _a2=(1-alpha)/a0;
    }
    public double Process(double x)
    {
        if(!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        var y=_b0*x+_z1; _z1=_b1*x-_a1*y+_z2; _z2=_b2*x-_a2*y; return y;
    }
    public void Reset()=>_z1=_z2=0;
}
