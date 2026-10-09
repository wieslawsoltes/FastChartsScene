using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastChartsScene.Explorer;

public sealed partial class App : Application
{
    public static Window MainWindow { get; private set; }=null!;
    public App() { InitializeComponent(); RequestedTheme=ApplicationTheme.Dark; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow=new Window { Title="FastChartsScene — Interactive Examples",Content=new TextBlock { Text="FastChartsScene\nScientific charting components",FontSize=28,Margin=new Thickness(32) } };
        MainWindow.Activate();
    }
}
