using System;
using System.Linq;
using Avalonia;
using CS2.Translator.Core.Helper;

namespace CS2.Translator.UI.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var enableDebug = args.Contains("-debug", StringComparer.OrdinalIgnoreCase);

        DebugLogger.Initialize(enableDebug);
        DebugLogger.Log("===============================================");
        DebugLogger.Log("CS2.Translator started");
        DebugLogger.Log($"OS: {Environment.OSVersion} ({DebugLogger.GetPlatformName()})");
        DebugLogger.Log($".NET Runtime: {Environment.Version}");
        DebugLogger.Log($"Data folder: {AppPaths.BaseDirectory}");
        DebugLogger.Log("===============================================");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                DebugLogger.LogException(ex, "AppDomain.UnhandledException");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DebugLogger.LogException(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "Unhandled exception in Main");
            throw;
        }
        finally
        {
            DebugLogger.Log("CS2.Translator shutdown");
        }
    }

    // Referenced by the Avalonia previewer, so it must stay public.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
