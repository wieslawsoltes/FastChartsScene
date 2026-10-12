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
        var tools=_toolbarScroll;
        var buttons=((StackPanel)tools.Content).Children.OfType<Button>();
        foreach(var button in buttons)
        {
            if(button.Content is not string title) continue;
            if(title=="Examples") button.Click+=(_,_)=>
            {
                if(ActualWidth>=680) return;
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
                var owner=_inspectorScroll;
                var scroll=new ScrollViewer { RequestedTheme=ElementTheme.Light, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,MaxHeight=470 };
                var content=new Grid(); content.RowDefinitions.Add(new() { Height=new GridLength(1,GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height=GridLength.Auto });
                content.Children.Add(scroll);
                try
                {
                    InspectorDialogOpen=true; owner.Content=null; scroll.Content=_inspector;
                    _inspectorPane.Children.Remove(_propertyHelp); content.Children.Add(_propertyHelp);
                    var dialog=new ContentDialog { Title="Chart inspector",RequestedTheme=ElementTheme.Light,Content=content,CloseButtonText="Done",XamlRoot=XamlRoot };
                    await dialog.ShowAsync();
                }
                catch(Exception error) { LastCommandError=error.Message; UpdateStatus(); }
                finally { scroll.Content=null; owner.Content=_inspector; content.Children.Remove(_propertyHelp); _inspectorPane.Children.Add(_propertyHelp); InspectorDialogOpen=false; }
            };
        }
    }
    private void UpdateCompactLayout()
    {
        if(_brand is Grid brand)
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
            if(node is Button button && (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)==text || button.Content is string name&&name==text)) return button;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            { var found=Find(VisualTreeHelper.GetChild(node,i),text); if(found!=null) return found; }
            return null;
        }
        var target=Find(Page??throw new InvalidOperationException("Explorer not initialized."),title)??throw new ArgumentException("Action not found: "+title);
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired=false,HorizontalAlignmentRatio=.5,VerticalAlignmentRatio=.5 });
    }
}
