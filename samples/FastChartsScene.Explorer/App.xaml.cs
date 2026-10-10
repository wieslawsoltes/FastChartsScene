using Microsoft.UI.Xaml;
namespace FastChartsScene.Explorer;
public sealed partial class App : Application
{
    public static Window MainWindow { get; private set; }=null!;
    public App() { InitializeComponent(); RequestedTheme=ApplicationTheme.Dark; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow=new Window { Title="FastChartsScene — Scientific Chart Explorer",Content=new ExplorerPage() };
        MainWindow.Activate();
    }
}
