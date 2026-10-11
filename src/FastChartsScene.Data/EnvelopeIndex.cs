using FastChartsScene.Core;

namespace FastChartsScene.Data;

/// <summary>Min/max tree over 64-point blocks, preserving extrema without a per-point tree allocation.</summary>
public sealed class EnvelopeIndex
{
    private const int BlockSize=64;
    private readonly IDataSource _data;
    private readonly int[] _min,_max;
    private readonly int _leaves;
    private readonly int[] _gaps;
    public EnvelopeIndex(IDataSource data)
    {
        _data=data; var blocks=(data.Count+BlockSize-1)/BlockSize; _leaves=1;
        while(_leaves<blocks) _leaves*=2;
        _min=Enumerable.Repeat(-1,_leaves*2).ToArray(); _max=(int[])_min.Clone();
        List<int> gaps=[];
        for(var i=0;i<data.Count;i++)
        {
            if(double.IsNaN(data.Y(i))) { gaps.Add(i); continue; }
            var n=_leaves+i/BlockSize;
            _min[n]=Min(_min[n],i); _max[n]=Max(_max[n],i);
        }
        for(var i=_leaves-1;i>0;i--) { _min[i]=Min(_min[i*2],_min[i*2+1]); _max[i]=Max(_max[i*2],_max[i*2+1]); }
        _gaps=gaps.ToArray();
    }
    private int Min(int a,int b)=>a<0?b:b<0?a:_data.Y(a)<=_data.Y(b)?a:b;
    private int Max(int a,int b)=>a<0?b:b<0?a:_data.Y(a)>=_data.Y(b)?a:b;
    public (int Minimum,int Maximum) Query(int start,int end)
    {
        if(start<0||end>_data.Count||end<start) throw new ArgumentOutOfRangeException(nameof(start));
        var min=-1; var max=-1;
        void Include(int i) { if(!double.IsNaN(_data.Y(i))) { min=Min(min,i); max=Max(max,i); } }
        while(start<end&&start%BlockSize!=0) Include(start++);
        while(end>start&&end%BlockSize!=0) Include(--end);
        var a=start/BlockSize+_leaves; var b=end/BlockSize+_leaves;
        while(a<b)
        {
            if((a&1)!=0) { min=Min(min,_min[a]); max=Max(max,_max[a]); a++; }
            if((b&1)!=0) { --b; min=Min(min,_min[b]); max=Max(max,_max[b]); }
            a/=2; b/=2;
        }
        return(min,max);
    }
    /// <summary>Screen-column envelope: endpoints, extrema, and every discontinuity are retained in source order.</summary>
    public IReadOnlyList<int> Select(Axis axis,int pixelWidth)
    {
        if(!_data.IsOrdered||_data.Count<2) return Enumerable.Range(0,_data.Count).ToArray();
        pixelWidth=Math.Clamp(pixelWidth,1,32768);
        var first=Math.Max(0,DataSearch.LowerBound(_data,axis.Range.Minimum)-1);
        var end=Math.Min(_data.Count,DataSearch.UpperBound(_data,axis.Range.Maximum)+1);
        if(end-first<=pixelWidth*4) return Enumerable.Range(first,Math.Max(0,end-first)).ToArray();
        SortedSet<int> selected=[];
        void Bucket(int a,int b)
        {
            if(b<=a) return;
            selected.Add(a); selected.Add(b-1);
            var mm=Query(a,b); if(mm.Minimum>=0) selected.Add(mm.Minimum); if(mm.Maximum>=0) selected.Add(mm.Maximum);
        }
        var start=first;
        for(var p=1;p<=pixelWidth;p++)
        {
            var x=axis.FromUnit(axis.Inverted?1-(double)p/pixelWidth:(double)p/pixelWidth);
            var stop=p==pixelWidth?end:Math.Clamp(DataSearch.LowerBound(_data,x),start,end);
            Bucket(start,stop); start=stop;
        }
        foreach(var gap in _gaps)
        {
            if(gap<first) continue; if(gap>=end) break;
            selected.Add(gap); if(gap>first) selected.Add(gap-1); if(gap+1<end) selected.Add(gap+1);
        }
        return selected.ToArray();
    }
}
