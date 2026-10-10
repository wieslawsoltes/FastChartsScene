using System.Text.Json;
using FastChartsScene.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif
namespace FastChartsScene.Explorer;
public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static string ActionBounds(string title)
    {
        FrameworkElement? Find(DependencyObject node)
        {
            if(node is Button button&&button.Content is string text&&text==title&&button.ActualWidth>0) return button;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) { var found=Find(VisualTreeHelper.GetChild(node,i)); if(found!=null) return found; }
            return null;
        }
        var control=Find(Page??throw new InvalidOperationException("Explorer not initialized."))??throw new ArgumentException("Visible action not found: "+title);
        var origin=control.TransformToVisual(null).TransformPoint(new Point(0,0));
        return JsonSerializer.Serialize(new RectD(origin.X,origin.Y,control.ActualWidth,control.ActualHeight),BrowserJsonContext.Default.RectD);
    }
}
