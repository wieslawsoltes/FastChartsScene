using Microsoft.Extensions.Logging;

namespace FastChartsScene.Explorer;

/// <summary>Bounded, dependency-light diagnostics for graphics initialization and errors. No chart data is logged.</summary>
internal sealed class RuntimeDiagnostics : ILoggerFactory
{
    public static void Initialize()
    {
        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory=new RuntimeDiagnostics();
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
    }
    public ILogger CreateLogger(string categoryName)=>new Logger(categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
    private sealed class Logger(string category) : ILogger
    {
        private int _written;
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Error||category.Contains("Graphics3DGL",StringComparison.Ordinal)&&level>=LogLevel.Debug;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> format)
        {
            if(!IsEnabled(level)||Interlocked.Increment(ref _written)>40) return;
            Console.WriteLine($"[{level}] {category}: {format(state,error)}{(error==null?"":"\n"+error)}");
        }
    }
}
