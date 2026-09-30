using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecondBrain.Desktop.Plugins;
using SecondBrain.Infrastructure;
using SecondBrain.Plugins.Search;
using SecondBrain.Plugins.Sdk;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;

namespace SecondBrain.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        SetupFileLogging();

        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json")
                .Build();

            // Kolejnosc z PLAN-PLUGINS.md 2.4: infrastruktura -> powloka -> pluginy (tylko wlaczone) -> provider.
            var services = new ServiceCollection();
            services.AddSecondBrainInfrastructure(config);
            services.AddSecondBrainShell();
            PluginManager.Discover(typeof(SearchPlugin).Assembly).ConfigureServices(services, config);
            App.Services = services.BuildServiceProvider();
            PluginRuntime.Services = App.Services;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void SetupFileLogging()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        // Avalonia's .LogToTrace() writes to System.Diagnostics.Trace - forward it into Serilog too.
        Trace.Listeners.Add(new SerilogTraceListener());
    }

    private sealed class SerilogTraceListener : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                Log.Information("{AvaloniaTrace}", message);
        }
    }
}
