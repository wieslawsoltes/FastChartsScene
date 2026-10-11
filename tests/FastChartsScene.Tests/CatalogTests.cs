using FastChartsScene.Core;
namespace FastChartsScene.Tests;
internal static partial class Program
{
    private static CatalogBrowserModel Catalog() => new([
        new("signals", "Realtime signals", "XY", "Three channels with continuous acquisition", ["2D", "Realtime"]),
        new("spectrum", "FFT spectrum", "DSP", "Frequency analysis of signals", ["2D", "FFT"]),
        new("surface", "Shaded surface", "3D", "Scientific surface grid", ["3D", "Grid"])]);
    static partial void RegisterCatalogTests()
    {
        Add("catalog search combines case-insensitive keywords and phrases", () => {
            var c=Catalog(); Equal(1,c.Filter("signals continuous").Count); Equal(0,c.Filter("signals grid").Count);
            True(c.Filter("\"FFT spectrum\"")[0].Id=="spectrum"); Equal(1,c.Filter("\"Three channels").Count);
            Equal(2,c.Filter("2d").Count); Equal(0,c.Filter("surface", "XY").Count);
        });
        Add("catalog tag any/all and categories combine without mutating input", () => {
            var c=Catalog(); string[] tags=["2D","Realtime"]; Equal(1,c.Filter(tags:tags).Count);
            Equal(2,c.Filter(tags:tags,matchAllTags:false).Count); True(tags.SequenceEqual(new[]{"2D","Realtime"}));
            Equal(1,c.Filter(category:"dsp",tags:["FFT"]).Count);
        });
        Add("catalog identical normalized queries reuse result lists", () => {
            var c=Catalog();var a=c.Filter("signals  continuous");var b=c.Filter(" CONTINUOUS signals ");
            True(ReferenceEquals(a,b));Equal(1,c.FilterPasses);
            c.SetFavorite("signals",true);c.Filter("signals continuous");Equal(2,c.FilterPasses);
        });
        Add("favorite changes notify once and roundtrip deterministically", () => {
            var a=Catalog();var events=0;a.FavoritesChanged+=(_,_)=>events++;
            a.SetFavorite("signals",true);a.SetFavorite("signals",true);a.SetFavorite("surface",true);Equal(2,events);
            Equal(2,a.Filter(favoritesOnly:true).Count);var text=a.ExportFavorites();var b=Catalog();b.ImportFavorites(text);True(text==b.ExportFavorites());
            b.ImportFavorites(text);a.SetFavorite("signals",false);Equal(1,a.FavoriteCount);
        });
        Add("invalid favorites never discard the prior list", () => {
            var c=Catalog();c.SetFavorite("signals",true);var original=c.ExportFavorites();
            foreach(var json in new[]{"[]","{}","{\"format\":\"FastChartsScene.Favorites\",\"version\":1,\"ids\":[\"missing\"]}",
                "{\"format\":\"FastChartsScene.Favorites\",\"version\":1,\"ids\":[\"signals\",\"signals\"]}",
                "{\"format\":\"FastChartsScene.Favorites\",\"version\":1,\"ids\":[],\"ids\":[]}"})
            { Throws<FormatException>(()=>c.ImportFavorites(json));True(c.ExportFavorites()==original); }
            Throws<FormatException>(()=>c.ImportFavorites(new string(' ',65537)));True(c.ExportFavorites()==original);
        });
        Add("catalog metadata owns tags and rejects duplicates and unbounded requests", () => {
            string[] tags=["Grid"];var entry=new CatalogEntry("a","Title","Group","Details",tags);tags[0]="changed";True(entry.Tags[0]=="Grid");
            Throws<ArgumentException>(()=>new CatalogBrowserModel([entry,entry]));
            var c=Catalog();Throws<ArgumentException>(()=>c.Filter(new string('x',513)));Throws<ArgumentException>(()=>c.SetFavorite("unknown",true));
        });
        Add("catalog cache distinguishes delimiter-containing queries and tags", () => {
            var c=new CatalogBrowserModel([new("a","a|b","x","",["ab","c"]),new("b","a b","x","",["a","bc"])]);
            True(c.Filter("a|b")[0].Id=="a");True(c.Filter("a b").Count==2);
            True(c.Filter(tags:["ab","c"])[0].Id=="a");True(c.Filter(tags:["a","bc"])[0].Id=="b");
        });
        Add("catalog ten-thousand-entry filter caches repeated work", () => {
            var c=new CatalogBrowserModel(Enumerable.Range(0,10000).Select(i=>new CatalogEntry("id"+i,"Example "+i,"category","preview",[i%2==0?"even":"odd"])));
            var a=c.Filter(tags:["even"]);Equal(5000,a.Count);for(var i=0;i<100;i++)True(ReferenceEquals(a,c.Filter(tags:["even"])));Equal(1,c.FilterPasses);
        });
    }
}
