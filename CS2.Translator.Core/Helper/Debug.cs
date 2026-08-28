namespace CS2.Translator.Core.Helper;

public static class DebugLogger
{
    private const long MaxLogBytes = 5_000_000;
    private const int RotationCheckInterval = 500;

    private static readonly object Lock = new();
    private static string? _logFilePath;
    private static int _writesSinceRotationCheck;

    public static bool Enabled { get; private set; }

    public static string? LogFilePath => _logFilePath;

    public static void Initialize(bool enableDebug)
    {
        Enabled = enableDebug;
        if (!Enabled)
            return;

        try
        {
            var logDir = AppPaths.EnsureLogDirectory();
            _logFilePath = Path.Combine(logDir, "debug.log");

            RotateIfNeeded();

            File.AppendAllText(_logFilePath, $"[Debug Start] {DateTime.Now}{Environment.NewLine}");
            Log($"Logger initialized on {GetPlatformName()} at {_logFilePath}");
        }
        catch (Exception ex)
        {
            Enabled = false;
            _logFilePath = null;
            Console.Error.WriteLine($"[DebugLogger] Initialization failed: {ex.Message}");
        }
    }

    public static void Log(string message, string? tag = null)
    {
        if (!Enabled)
            return;

        var formatted = tag is null
            ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}"
            : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {message}";

        Console.WriteLine(formatted);

        if (_logFilePath is null)
            return;

        try
        {
            lock (Lock)
            {
                File.AppendAllText(_logFilePath, formatted + Environment.NewLine);

                if (++_writesSinceRotationCheck >= RotationCheckInterval)
                {
                    _writesSinceRotationCheck = 0;
                    RotateIfNeededCore();
                }
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void LogException(Exception ex, string? context = null)
    {
        // Failures are worth surfacing even with debug logging off, but only to stderr.
        if (!Enabled)
        {
            Console.Error.WriteLine($"[{context ?? "Unhandled"}] {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Log($"[EXCEPTION] {context ?? "Unhandled"}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }

    public static void RotateIfNeeded()
    {
        lock (Lock)
        {
            RotateIfNeededCore();
        }
    }

    private static void RotateIfNeededCore()
    {
        if (_logFilePath is null)
            return;

        try
        {
            var info = new FileInfo(_logFilePath);
            if (!info.Exists || info.Length <= MaxLogBytes)
                return;

            var archive = Path.Combine(
                info.DirectoryName!,
                $"debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            File.Move(_logFilePath, archive, overwrite: true);
            File.WriteAllText(_logFilePath, $"[Debug Rotated] {DateTime.Now}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DebugLogger] Rotation failed: {ex.Message}");
        }
    }

    /// <summary>Opens the data folder in the platform file manager. Always available so users can find the config.</summary>
    public static void OpenLogFolder()
    {
        try
        {
            var folder = _logFilePath is not null
                ? Path.GetDirectoryName(_logFilePath)
                : AppPaths.EnsureBaseDirectory();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                folder = AppPaths.EnsureBaseDirectory();

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            };

            if (OperatingSystem.IsLinux())
            {
                startInfo.FileName = "xdg-open";
                startInfo.Arguments = $"\"{folder}\"";
                startInfo.UseShellExecute = false;
            }

            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LogException(ex, "OpenLogFolder");
        }
    }

    public static string GetPlatformName() =>
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsLinux() ? "Linux" :
        OperatingSystem.IsMacOS() ? "macOS" :
        "Unknown";
}
