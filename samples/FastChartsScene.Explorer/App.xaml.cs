using Microsoft.UI.Xaml;
namespace FastChartsScene.Explorer;
public sealed partial class App : Application
{
    public static Window MainWindow { get; private set; }=null!;
    public App()
    {
        RuntimeDiagnostics.Initialize();
        InitializeComponent(); RequestedTheme=ApplicationTheme.Dark;
        UnhandledException+=(_,e)=>Console.Error.WriteLine("Unhandled application exception: "+e.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var explorer=new ExplorerPage(); explorer.InitializeResponsiveLayout();
        MainWindow=new Window { Title="FastChartsScene — Scientific Chart Explorer",Content=explorer };
        MainWindow.Activate();
    }
}
