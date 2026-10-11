using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.SignalProcessing;

namespace FastChartsScene.Examples;

public sealed record ChartExample(string Id,string Category,string Title,string Description,Func<ChartDocument> Create)
{
    public override string ToString()=>Title;
}

/// <summary>Deterministic, independently reusable scientific chart fixtures. All data is synthetic.</summary>
public static class ExampleCatalog
{
    public static IReadOnlyList<ChartExample> All { get; }=Array.AsReadOnly<ChartExample>([
        new("signals","XY charts","Multichannel signals","Three independently styled, sampled waveforms. Drag to pan; wheel to zoom; hover for original sample values.",()=>Signals()),
        new("streaming","XY charts","Live scrolling traces","Start the acquisition toolbar command to feed a bounded rolling buffer. Generated data is a synthetic signal, not a connected instrument.",()=>Signals("Live scrolling acquisition")),
        new("million","XY charts","One million samples","Extrema-preserving, viewport-indexed reduction retains isolated spikes and NaN breaks without uploading a million vertices.",Million),
        new("scatter","XY charts","Scatter plot","A deterministic Lissajous point cloud with original-data cursor hit testing.",Scatter),
        new("spline","XY charts","Monotone cubic curves","Shape-preserving cubic interpolation with markers at the original samples only.",()=>Interpolation(LineInterpolation.MonotoneCubic)),
        new("steps","XY charts","Step and area series","Step-after interpolation and an area baseline with correctly split sign crossings.",()=>Interpolation(LineInterpolation.StepAfter)),
        new("band","XY charts","Confidence bands","Paired lower and upper data boundaries with translucent fill and central estimate.",Band),
        new("errors","XY charts","Error bars","Asymmetric uncertainty intervals with caps and sample markers.",Errors),
        new("bars","XY charts","Stacked columns","Positive and negative stacks maintain separate accumulators for each category.",()=>Bars(false)),
        new("horizontal","XY charts","Horizontal bars","Category and value coordinates are transposed consistently for rendering and cursor queries.",()=>Bars(true)),
        new("financial","Financial","Candlestick chart","Synthetic OHLC prices with a simple moving average. Data is illustrative and not market information.",()=>Financial(FinancialStyle.Candlestick)),
        new("ohlc","Financial","OHLC bars","Open and close ticks, high-low stems, and a moving-average overlay.",()=>Financial(FinancialStyle.Ohlc)),
        new("heatmap","Intensity & contours","Intensity heatmap","Regular scalar grid with a continuous color palette and an intentional missing-data hole.",Heatmap),
        new("contours","Intensity & contours","Contour map","Marching-squares contours on a regular scalar field, including ambiguous saddle resolution.",Contours),
        new("spectrum","Signal processing","FFT spectrum","A calibrated Hann-windowed amplitude spectrum of synthetic 37 Hz and 113 Hz sinusoidal components.",Spectrum),
        new("spectrogram","Signal processing","Spectrogram","Short-time Fourier transform of a synthetic chirp; horizontal time, vertical frequency, and intensity in decibels.",Spectrogram),
        new("polar","Circular charts","Polar curves","Angle-radius curves, radial ticks, and a circular graticule.",Polar),
        new("smith","Circular charts","Smith impedance chart","Normalized complex impedance mapped to reflection coefficient; resistance and reactance grid curves.",Smith),
        new("stacked","Axes & layout","Stacked axes","Three independently scaled, weighted panes sharing the X axis. Vertical pan/zoom affects only the pane under the pointer.",()=>Panes(false)),
        new("segmented","Axes & layout","Segmented axes","Two overlaid Y axes share the upper segment, while another axis occupies the lower segment.",()=>Panes(true)),
        new("log","Axes & layout","Logarithmic axes","Logarithmic coordinate transforms and logarithmic zoom anchoring across several decades.",Logarithmic),
        new("dates","Axes & layout","Date and time axis","UTC timestamps on the X axis with range-sensitive formatting.",Dates),
        new("surface","3D charts","Shaded surface","Lit, colored surface triangles with depth testing. Drag to orbit; wheel to dolly; switch projection in the inspector.",()=>Grid3D("surface")),
        new("waterfall","3D charts","Waterfall traces","A stack of three-dimensional line traces with depth-tested tube geometry.",()=>Grid3D("waterfall")),
        new("bars3d","3D charts","3D bar chart","Independent colored boxes arranged on a scalar grid.",()=>Grid3D("bars3d")),
        new("helix","3D charts","3D line and scatter","A spatial helix, rendered as connected segments and point markers.",Helix),
        new("mesh","3D charts","Triangle mesh","An independently generated octahedron mesh with per-vertex colors and face lighting.",Mesh),
        new("pie","3D charts","3D pie chart","Depth-tested solid sectors with top, bottom, side, and radial faces.",()=>Pie(false)),
        new("donut","3D charts","Exploded doughnut","Extruded annular sectors with inner walls and per-slice explosion.",()=>Pie(true))
    ]);
    public static ChartExample Find(string id)=>All.FirstOrDefault(e=>e.Id==id)??throw new ArgumentException($"Unknown example '{id}'.",nameof(id));
    private static ChartDocument Document(string title,string x="Time",string y="Amplitude")
    {
        var d=new ChartDocument { Title=title,Subtitle="Synthetic data · drag to pan · wheel to zoom" }; d.XAxis.Title=x; d.YAxis.Title=y; return d;
    }
    private static UniformData Wave(int count,double step,Func<double,double> f,double start=0)=>new(start,step,Enumerable.Range(0,count).Select(i=>f(start+i*step)).ToArray());
    private static LineSeries Line(string title,IDataSource data,int color=0)=>new() { Title=title,Data=data,Color=Palette.Series[color%Palette.Series.Length],StrokeWidth=1.8 };
    public static ChartDocument Signals(string title="Multichannel signals")
    {
        var d=Document(title); d.XAxis.Units="s"; d.YAxis.Units="V"; d.XAxis.Range=new(0,10); d.YAxis.Range=new(-1.6,1.6);
        d.Series.Add(Line("Carrier",Wave(12000,10.0/11999,t=>Math.Sin(t*7)*(.7+.22*Math.Sin(t*.8))),0));
        d.Series.Add(Line("Modulated",Wave(12000,10.0/11999,t=>.55*Math.Cos(t*11)+.18*Math.Sin(t*29)),1));
        d.Series.Add(Line("Reference",Wave(12000,10.0/11999,t=>.35*Math.Sin(t*3)+.7),3));
        return d;
    }
    private static ChartDocument Million()
    {
        var d=Document("One million samples"); d.XAxis.Range=new(0,100); d.YAxis.Range=new(-2.3,2.3); d.XAxis.Units="s";
        var values=Enumerable.Range(0,1_000_000).Select(i=>Math.Sin(i*.0017)*.65+.18*Math.Cos(i*.017)).ToArray();
        values[153729]=2; values[682413]=-2; for(var i=425000;i<430000;i++) values[i]=double.NaN;
        d.Series.Add(Line("1,000,000 samples",new UniformData(0,.0001,values))); return d;
    }
    private static ChartDocument Scatter()
    {
        var d=Document("Lissajous scatter","X","Y"); d.XAxis.Range=new(-1.25,1.25); d.YAxis.Range=new(-1.25,1.25);
        var points=Enumerable.Range(0,1200).Select(i=>new PointD(Math.Sin(i*.043),Math.Cos(i*.037))).ToArray();
        d.Series.Add(new LineSeries { Title="Samples",Data=XyData.FromPoints(points),ShowLine=false,Marker=MarkerShape.Circle,MarkerSize=3.5,Color=Palette.Series[0] }); return d;
    }
    private static ChartDocument Interpolation(LineInterpolation kind)
    {
        var d=Document(kind==LineInterpolation.MonotoneCubic?"Shape-preserving interpolation":"Stepped area series","Sample","Value");
        d.XAxis.Range=new(0,12); d.YAxis.Range=new(-2,3);
        d.Series.Add(new LineSeries { Title="Original samples",Data=Wave(13,1,t=>1.3*Math.Sin(t*.7)+.6),Interpolation=kind,FillToBaseline=kind==LineInterpolation.StepAfter,Marker=MarkerShape.Diamond,MarkerSize=7,StrokeWidth=2.5 }); return d;
    }
    private static ChartDocument Band()
    {
        var d=Document("Confidence interval"); d.XAxis.Range=new(0,10); d.YAxis.Range=new(-2,2);
        var low=Wave(600,10.0/599,t=>Math.Sin(t)-.2-.1*Math.Cos(t*2)); var high=Wave(600,10.0/599,t=>Math.Sin(t)+.2+.1*Math.Cos(t*2));
        d.Series.Add(new BandSeries { Title="Uncertainty band",Data=low,Upper=high,Color=Palette.Series[0] }); d.Series.Add(Line("Estimate",Wave(600,10.0/599,Math.Sin),1)); return d;
    }
    private static ChartDocument Errors()
    {
        var d=Document("Asymmetric error intervals","Sample","Measurement"); d.XAxis.Range=new(-1,20); d.YAxis.Range=new(-1,4);
        var error=new ErrorBarSeries { Title="Measurement ± uncertainty",Data=Wave(20,1,t=>1.2+Math.Sin(t*.35)),Color=Palette.Series[1] };
        error.SetErrors(Enumerable.Range(0,20).Select(i=>.1+.2*(i%3)).ToArray(),Enumerable.Range(0,20).Select(i=>.25+.12*(i%4)).ToArray()); d.Series.Add(error); return d;
    }
    private static ChartDocument Bars(bool horizontal)
    {
        var d=Document(horizontal?"Horizontal stacked bars":"Positive and negative stacks","Category","Value");
        var category=horizontal?d.YAxis:d.XAxis; var value=horizontal?d.XAxis:d.YAxis;
        category.Range=new(-.7,7.7); category.Scale=AxisScale.Category; category.Categories=["A","B","C","D","E","F","G","H"]; category.Title="Category";
        value.Range=new(-6,10); value.Title="Value";
        for(var channel=0;channel<3;channel++) d.Series.Add(new BarSeries { Title=$"Group {channel+1}",Data=Wave(8,1,t=>Math.Sin(t*.75+channel)*2.7+1),Horizontal=horizontal,StackGroup="total",BarWidth=.68,Color=Palette.Series[channel] }); return d;
    }
    private static ChartDocument Financial(FinancialStyle style)
    {
        var d=Document("Synthetic OHLC prices","Session","Price"); d.XAxis.Range=new(-2,102); d.YAxis.Range=new(85,120); d.YAxis.Units="units";
        var prices=new List<OhlcPoint>(); var previous=100.0;
        for(var i=0;i<100;i++) { var close=100+8*Math.Sin(i*.07)+3*Math.Sin(i*.41); prices.Add(new(i,previous,Math.Max(previous,close)+1.3,Math.Min(previous,close)-1.1,close,100+i*3)); previous=close; }
        var f=new FinancialSeries { Title="Synthetic OHLC",Style=style }; f.SetData(prices.ToArray()); d.Series.Add(f);
        d.Series.Add(Line("SMA (10)",new UniformData(0,1,Indicators.Sma(prices.Select(p=>p.Close).ToArray(),10)),1)); return d;
    }
    private static ScalarGrid Field(int columns=65,int rows=49,bool hole=false)
    {
        var values=new double[columns*rows];
        for(var row=0;row<rows;row++) for(var col=0;col<columns;col++)
        {
            var x=-3+6.0*col/(columns-1); var y=-3+6.0*row/(rows-1);
            values[row*columns+col]=hole&&x>.7&&x<1.3&&y>1&&y<1.7?double.NaN:Math.Sin(x*x+y*y)*Math.Exp(-(x*x+y*y)*.14)+.3*Math.Cos(x*2-y);
        }
        return new(columns,rows,-3,3,-3,3,values);
    }
    private static ChartDocument Heatmap()
    {
        var d=Document("Intensity heatmap","X position","Y position"); d.ShowLegend=false; d.XAxis.Range=new(-3,3); d.YAxis.Range=new(-3,3); d.Series.Add(new HeatmapSeries { Title="Scalar field",Grid=Field(hole:true) }); return d;
    }
    private static ChartDocument Contours()
    {
        var d=Document("Contour lines","X position","Y position"); d.ShowLegend=false; d.XAxis.Range=new(-3,3); d.YAxis.Range=new(-3,3);
        var s=new ContourSeries { Title="Isolines",Grid=Field(),StrokeWidth=1.8 }; s.SetLevels(Enumerable.Range(-5,11).Select(i=>i*.2).ToArray()); d.Series.Add(s); return d;
    }
    private static ChartDocument Spectrum()
    {
        const double rate=1024; var data=Enumerable.Range(0,4096).Select(i=>Math.Sin(2*Math.PI*37*i/rate)+.45*Math.Sin(2*Math.PI*113*i/rate)).ToArray();
        var spectrum=Fourier.Spectrum(data,rate); var d=Document("Calibrated FFT spectrum","Frequency","Amplitude"); d.XAxis.Units="Hz"; d.YAxis.Units="V"; d.XAxis.Range=new(0,250); d.YAxis.Range=new(0,1.15);
        d.Series.Add(Line("Hann spectrum",new XyData(spectrum.Frequencies,spectrum.Amplitudes))); return d;
    }
    private static ChartDocument Spectrogram()
    {
        const double rate=1024; var samples=Enumerable.Range(0,8192).Select(i=> { var t=i/rate; return Math.Sin(2*Math.PI*(25*t+14*t*t))+.3*Math.Sin(2*Math.PI*175*t); }).ToArray();
        var stft=Fourier.Spectrogram(samples,rate,256,64); var values=new double[stft.TimeBins*stft.FrequencyBins];
        for(var t=0;t<stft.TimeBins;t++) for(var bin=0;bin<stft.FrequencyBins;bin++) values[bin*stft.TimeBins+t]=20*Math.Log10(Math.Max(1e-5,stft.Values[t*stft.FrequencyBins+bin]));
        var d=Document("Chirp spectrogram","Time","Frequency"); d.XAxis.Units="s"; d.YAxis.Units="Hz"; d.XAxis.Range=new(0,(stft.TimeBins-1)*stft.TimeStep); d.YAxis.Range=new(0,512); d.ShowLegend=false;
        d.Series.Add(new HeatmapSeries { Title="Amplitude (dB)",Grid=new(stft.TimeBins,stft.FrequencyBins,0,d.XAxis.Range.Maximum,0,512,values) }); return d;
    }
    private static ChartDocument Polar()
    {
        var d=Document("Polar response","Angle","Radius"); d.View=ChartView.Polar; d.YAxis.Range=new(0,1); d.Subtitle="Angle in degrees · normalized radius";
        d.Series.Add(Line("Response",Wave(721,.5,t=>.2+.75*Math.Abs(Math.Cos(t*Math.PI/90))))); d.Series.Add(Line("Reference",Wave(721,.5,_=>.6),1)); return d;
    }
    private static ChartDocument Smith()
    {
        var d=Document("Smith impedance chart","Resistance","Reactance"); d.View=ChartView.Smith; d.Subtitle="Normalized complex impedance · z = r + jx";
        d.Series.Add(Line("Impedance sweep",XyData.FromPoints(Enumerable.Range(0,800).Select(i=> { var t=i/799.0; return new PointD(.2+2*t,.8*Math.Sin(t*8)-1.1); })),0)); return d;
    }
    private static ChartDocument Panes(bool segmented)
    {
        var d=Document(segmented?"Segmented axes":"Stacked axes"); d.AxisLayout=segmented?AxisLayout.Segmented:AxisLayout.Stacked; d.XAxis.Range=new(0,10);
        d.YAxis.Title="Voltage"; d.YAxis.Units="V"; d.YAxis.Range=new(-1.5,1.5); d.YAxis.Weight=2;
        d.YAxes.Add(new("current") { Title="Current",Units="mA",Range=new(-100,100),Side=segmented?AxisSide.Far:AxisSide.Near });
        d.YAxes.Add(new("temperature") { Title="Temperature",Units="°C",Range=new(15,40) });
        if(segmented) { d.Segments.Add(new("Electrical",2,["y","current"])); d.Segments.Add(new("Thermal",1,["temperature"])); }
        d.Series.Add(Line("Voltage",Wave(1000,10.0/999,t=>Math.Sin(t*4)),0));
        var current=Line("Current",Wave(1000,10.0/999,t=>65*Math.Cos(t*3)),1); current.YAxisId="current"; d.Series.Add(current);
        var temperature=Line("Temperature",Wave(1000,10.0/999,t=>25+5*Math.Sin(t*.4)),2); temperature.YAxisId="temperature"; d.Series.Add(temperature); return d;
    }
    private static ChartDocument Logarithmic()
    {
        var d=Document("Logarithmic response","Frequency","Magnitude"); d.XAxis.Range=new(1,10000); d.XAxis.Scale=AxisScale.Logarithmic; d.YAxis.Range=new(.001,2); d.YAxis.Scale=AxisScale.Logarithmic;
        d.Series.Add(Line("Low-pass magnitude",XyData.FromPoints(Enumerable.Range(0,1000).Select(i=> { var x=Math.Pow(10,i*4.0/999); return new PointD(x,1/Math.Sqrt(1+x*x/10000)); })))); return d;
    }
    private static ChartDocument Dates()
    {
        const double start=1728000000; var d=Document("UTC time series","Time (UTC)","Temperature"); d.XAxis.Range=new(start,start+3600); d.XAxis.Scale=AxisScale.DateTime; d.YAxis.Range=new(18,29);
        d.Series.Add(Line("Sensor",Wave(601,6,t=>23+3*Math.Sin((t-start)/600)+Math.Sin((t-start)/77)*.4,start))); return d;
    }
    private static ChartDocument Grid3D(string kind)
    {
        var d=Document(kind=="surface"?"Shaded surface":kind=="waterfall"?"Waterfall traces":"3D scalar bars","X","Height"); d.View=ChartView.ThreeD; d.Subtitle="Drag to orbit · wheel to dolly";
        d.XAxis.Range=new(-3,3); d.YAxis.Range=new(-1.4,1.4); d.ZAxis.Range=new(-3,3); d.ZAxis.Title="Z"; d.ShowLegend=false;
        var grid=kind=="bars3d"?Field(12,12):kind=="waterfall"?Field(65,24):Field(65,65);
        d.Series.Add(kind=="surface"?new SurfaceSeries3D { Grid=grid }:kind=="waterfall"?new WaterfallSeries3D { Grid=grid }:new BarSeries3D { Grid=grid }); return d;
    }
    private static ChartDocument Helix()
    {
        var d=Document("Spatial helix","X","Y"); d.View=ChartView.ThreeD; d.XAxis.Range=new(-1.2,1.2); d.YAxis.Range=new(-1.2,1.2); d.ZAxis.Range=new(-1.2,1.2); d.ZAxis.Title="Z";
        var line=new PointLineSeries3D { Title="Helix",Color=Palette.Series[0],PointSize=2,StrokeWidth=2 }; line.SetData(Enumerable.Range(0,500).Select(i=>new Point3D(Math.Cos(i*.06),i/250.0-1,Math.Sin(i*.06))).ToArray()); d.Series.Add(line); return d;
    }
    private static ChartDocument Mesh()
    {
        var d=Document("Triangle mesh","X","Y"); d.View=ChartView.ThreeD; d.XAxis.Range=new(-1.5,1.5); d.YAxis.Range=new(-1.5,1.5); d.ZAxis.Range=new(-1.5,1.5); d.ZAxis.Title="Z";
        Point3D[] points=[new(0,1,0),new(1,0,0),new(0,0,1),new(-1,0,0),new(0,0,-1),new(0,-1,0)];
        d.Series.Add(new MeshSeries3D { Title="Octahedron",Mesh=new(points.Select((p,i)=>new MeshVertex(p,Palette.Series[i])).ToArray(),[0,1,2,0,2,3,0,3,4,0,4,1,5,2,1,5,3,2,5,4,3,5,1,4]) }); return d;
    }
    private static ChartDocument Pie(bool donut)
    {
        var d=Document(donut?"Exploded doughnut":"3D pie chart"); d.View=ChartView.Pie3D; d.ShowLegend=false; d.Subtitle="Synthetic composition · drag to orbit";
        var pie=new PieSeries { Title="Composition",InnerRadius=donut?.5:0,Explosion=donut?.14:0,Height=.25 };
        pie.SetData([new("Alpha",36,Palette.Series[0]),new("Beta",25,Palette.Series[1]),new("Gamma",18,Palette.Series[2]),new("Delta",13,Palette.Series[3]),new("Epsilon",8,Palette.Series[4])]); d.Series.Add(pie); return d;
    }
}
