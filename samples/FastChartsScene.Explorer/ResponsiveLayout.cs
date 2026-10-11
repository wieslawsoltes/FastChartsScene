using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
#if __WASM__
using System.Runtime.InteropServices.JavaScript;
#endif

namespace FastChartsScene.Explorer;

public sealed partial class ExplorerPage
{
    private bool _responsiveInitialized;
    internal bool InspectorDialogOpen { get; private set; }
    internal bool CompactExamplesOpen { get; private set; }

    internal void InitializeResponsiveLayout()
    {
        if(_responsiveInitialized) return; _responsiveInitialized=true;
        SizeChanged+=(_,_)=>UpdateCompactLayout();
        Chart.SizeChanged+=(_,_)=>UpdateCompactLayout();
        Loaded+=(_,_)=>UpdateCompactLayout();
        // The toolbar and pane controls are created by the page constructor, before loading.
        var tools=(ScrollViewer)_root.Children[1];
        var buttons=((StackPanel)tools.Content).Children.OfType<Button>();
        foreach(var button in buttons)
        {
            if(button.Content is not string title) continue;
            if(title=="Examples") button.Click+=(_,_)=>
            {
                if(ActualWidth>=680) return;
                _galleryVisible=true;
                var menu=new MenuFlyout();
                foreach(var example in FastChartsScene.Examples.ExampleCatalog.All)
                {
                    var item=new MenuFlyoutItem { Text=example.Title };
                    item.Click+=(_,_)=>Execute(()=>SelectExample(example.Id)); menu.Items.Add(item);
                }
                menu.Opened+=(_,_)=>CompactExamplesOpen=true;
                menu.Closed+=(_,_)=>CompactExamplesOpen=false;
                menu.ShowAt(button);
            };
            if(title=="Inspector") button.Click+=async(_,_)=>
            {
                if(ActualWidth>=1100||InspectorDialogOpen) return;
                _inspectorVisible=true;
                var owner=(ScrollViewer)_workspace.Children[2];
                var scroll=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,MaxHeight=560 };
                try
                {
                    InspectorDialogOpen=true; owner.Content=null; scroll.Content=_inspector;
                    var dialog=new ContentDialog { Title="Chart inspector",Content=scroll,CloseButtonText="Done",XamlRoot=XamlRoot };
                    await dialog.ShowAsync();
                }
                catch(Exception error) { LastCommandError=error.Message; UpdateStatus(); }
                finally { scroll.Content=null; owner.Content=_inspector; InspectorDialogOpen=false; }
            };
        }
    }
    private void UpdateCompactLayout()
    {
        if(_root.Children[0] is Grid brand)
        {
            if(brand.Children[0] is StackPanel identity&&identity.Children.Count>2)
                identity.Children[2].Visibility=ActualWidth>=820?Visibility.Visible:Visibility.Collapsed;
            if(brand.Children.Count>1) brand.Children[1].Visibility=ActualWidth>=1050?Visibility.Visible:Visibility.Collapsed;
        }
        // A horizontally unconstrained ScrollViewer otherwise measures this TextBlock at
        // infinite width even with Wrap enabled. Use the actual chart column width.
        if(Chart.ActualWidth>32) _description.MaxWidth=Chart.ActualWidth-32;
    }
}

public static partial class BrowserBridge
{
#if __WASM__
    [JSExport]
#endif
    public static bool InspectorIsOpen()=>Page?.InspectorDialogOpen??false;
#if __WASM__
    [JSExport]
#endif
    public static bool CompactExamplesIsOpen()=>Page?.CompactExamplesOpen??false;
#if __WASM__
    [JSExport]
#endif
    public static void BringActionIntoView(string title)
    {
        static Button? Find(DependencyObject node,string text)
        {
            if(node is Button button&&button.Content is string name&&name==text) return button;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            { var found=Find(VisualTreeHelper.GetChild(node,i),text); if(found!=null) return found; }
            return null;
        }
        var target=Find(Page??throw new InvalidOperationException("Explorer not initialized."),title)??throw new ArgumentException("Action not found: "+title);
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired=false,HorizontalAlignmentRatio=.5,VerticalAlignmentRatio=.5 });
    }
}
