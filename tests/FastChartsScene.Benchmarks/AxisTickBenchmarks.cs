using FastChartsScene.Core;

internal static class AxisTickBenchmarks
{
    internal static void Run(Action<string, Func<double>> measure, List<object> measurements)
    {
        var axes = Enumerable.Range(0,128).Select(i=>new Axis("x"+i) { Range=new(-3.1,94.7) }).ToArray();
        // Frozen previous linear-tick loop, with the same range, formatter and tick target.
        static IReadOnlyList<AxisTick> Previous(Axis axis)
        {
            var step=Axis.NiceStep(Math.Max(double.Epsilon,axis.Range.Span/6));
            var first=Math.Ceiling(axis.Range.Minimum/step-1e-12)*step;List<AxisTick> ticks=[];
            for(var i=0;i<100;i++)
            {
                var v=first+i*step;if(!double.IsFinite(v)||v>axis.Range.Maximum+step*1e-8)break;
                if(v<axis.Range.Minimum-step*1e-8)continue;if(Math.Abs(v)<step*1e-10)v=0;
                if(ticks.Count>0&&v<=ticks[^1].Value)continue;
                ticks.Add(new(v,axis.ToUnit(v),axis.Format(v,step)));
            }
            return ticks;
        }
        foreach(var axis in axes)if(!Previous(axis).SequenceEqual(axis.Ticks()))throw new InvalidOperationException("Cached tick labels differ from previous generation.");
        double Uncached(){double count=0;foreach(var axis in axes)count+=Previous(axis).Count;return count;}
        double Cached(){double count=0;foreach(var axis in axes)count+=axis.Ticks().Count;return count;}
        measure("axis-labels-previous-128",Uncached);measure("axis-labels-retained-128",Cached);
        measurements.Add(new { name="axis-tick-cache",axes=axes.Length,generationPasses=axes.Sum(a=>a.TickGenerationCount),
            note="Identical unchanged-axis requests only. Cold generation and changed-axis updates are not cache hits. CPU only, not full-frame or GPU performance." });
    }
}
