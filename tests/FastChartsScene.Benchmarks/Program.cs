using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Data;
using FastChartsScene.Rendering;
using FastChartsScene.SignalProcessing;

// Diagnostic microbenchmarks, not performance acceptance thresholds. The baseline reproduces
// the previous implementation; both paths consume identical input and verify output checksums.
using var document=new ChartDocument(); document.XAxis.Range=new(49,51); document.YAxis.Range=new(49,51);
var grid=new ScalarGrid(1024,1024,0,100,0,100,Enumerable.Range(0,1024*1024).Select(i=>Math.Sin(i*.1)).ToArray());
var heat=new HeatmapSeries { Grid=grid }; var scale=heat.ResolveColorScale();
var layout=ChartLayout.Create(document,960,600);
double Exhaustive()
{
    var checksum=0.0;
    for(var r=0;r<grid.Rows;r++) for(var c=0;c<grid.Columns;c++)
    {
        var color=scale.Color(grid[c,r]);
        if(color.A>0 && IntensityGeometry.TryCellBounds(layout,document.XAxis,document.YAxis,grid,c,r,out var cell))
            checksum+=cell.X+cell.Y+cell.Width+cell.Height+color.R;
    }
    return checksum;
}
double Culled()
{
    var view=IntensityViewport.Create(layout,document.XAxis,document.YAxis,grid); var checksum=0.0;
    foreach(var r in view.Rows.Span) foreach(var c in view.Columns.Span)
    {
        var color=scale.Color(grid[c.Index,r.Index]);
        if(color.A>0) checksum+=c.Start+r.Start+c.Length+r.Length+color.R;
    }
    return checksum;
}
var buffer=new RollingBuffer(100000); var samples=Enumerable.Range(0,100000).Select(i=>(double)i).ToArray(); buffer.Append(samples);
double OldSnapshot()
{
    var copy=new double[samples.Length]; for(var i=0;i<copy.Length;i++) copy[i]=samples[i%samples.Length];
    var snapshot=new UniformData(0,1,copy); return snapshot.Y(snapshot.Count-1);
}
double NewSnapshot() { var snapshot=buffer.Snapshot(); return snapshot.Y(snapshot.Count-1); }
var expected=Exhaustive(); if(Math.Abs(expected-Culled())>1e-7) throw new InvalidOperationException("Viewport checksums differ.");
if(OldSnapshot()!=NewSnapshot()) throw new InvalidOperationException("Snapshot results differ.");
var measurements=new List<object>();
void Measure(string name,Func<double> action)
{
    for(var i=0;i<6;i++) action();
    var elapsed=new double[15]; var allocations=new long[15]; var checksum=0.0;
    for(var i=0;i<elapsed.Length;i++)
    {
        var allocated=GC.GetAllocatedBytesForCurrentThread(); var clock=Stopwatch.GetTimestamp();
        checksum=action(); elapsed[i]=Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        allocations[i]=GC.GetAllocatedBytesForCurrentThread()-allocated;
    }
    Array.Sort(elapsed); Array.Sort(allocations);
    measurements.Add(new { name,medianMilliseconds=elapsed[7],medianAllocatedBytes=allocations[7],checksum });
    Console.WriteLine(FormattableString.Invariant($"{name}: median {elapsed[7]:F4} ms; {allocations[7]} allocated bytes; checksum {checksum:R}"));
}
Measure("heatmap-exhaustive",Exhaustive); Measure("heatmap-visible-cells",Culled);
Measure("rolling-snapshot-two-copies",OldSnapshot); Measure("rolling-snapshot-owned",NewSnapshot);
var signal=Enumerable.Range(0,8192).Select(i=>Math.Sin(i*.31)+.3*Math.Sin(i*.017)).ToArray();
double[] OldStft()
{
    const int length=256,hop=64; var columns=1+(signal.Length-length)/hop; var rows=length/2+1; var data=new double[columns*rows];
    for(var col=0;col<columns;col++)
    {
        var spectrum=Fourier.Spectrum(signal.AsSpan(col*hop,length),1024);
        for(var row=0;row<rows;row++) data[row*columns+col]=20*Math.Log10(Math.Max(spectrum.Amplitudes[row],1e-12));
    }
    return data;
}
if(!OldStft().SequenceEqual(Fourier.Spectrogram(signal,1024).Values)) throw new InvalidOperationException("STFT values differ from the per-window reference.");
Measure("stft-per-window-spectrum",()=>OldStft().Sum());
Measure("stft-reused-scratch",()=>Fourier.Spectrogram(signal,1024).Values.Sum());
SampleQueryBenchmarks.Run(Measure, measurements);
CatalogBenchmarks.Run(Measure, measurements);
var output=Path.GetFullPath(args.Length>0?args[0]:"artifacts/performance.json"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output,JsonSerializer.Serialize(new { runtime=Environment.Version.ToString(),os=Environment.OSVersion.ToString(),
    note="CPU microbenchmarks on one CI runner; no hardware GPU, full-frame, latency or reference-product performance claim.",
    sourceCells=1024*1024,visibleCells=IntensityViewport.Create(layout,document.XAxis,document.YAxis,grid).CellCount,measurements },new JsonSerializerOptions { WriteIndented=true }));
