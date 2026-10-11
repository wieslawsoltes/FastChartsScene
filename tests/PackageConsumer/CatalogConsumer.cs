using FastChartsScene.Core;
using FastChartsScene.Controls.Uno;
namespace PackageConsumer;
public static class CatalogConsumer
{
    public static CatalogBrowserControl Create()
    {
        var gallery = new CatalogBrowserControl();
        gallery.SetModel(new CatalogBrowserModel([new("demo", "Demo", "Examples", "An independent host catalog", ["Custom"])]));
        gallery.OpenRequested += (_, entry) => Console.WriteLine(entry.Id);
        return gallery;
    }
}
