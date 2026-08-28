namespace CS2.Translator.Core.Helper;

/// <summary>
/// Single source of truth for where config, cache and logs live.
/// On Linux SpecialFolder.ApplicationData already resolves to $XDG_CONFIG_HOME (or ~/.config),
/// so one lookup covers both platforms and matches what the README documents.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "CS2-Translator";

    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create),
        AppFolderName);

    public static string ConfigFile => Path.Combine(BaseDirectory, "config.json");

    public static string LogDirectory => Path.Combine(BaseDirectory, "logs");

    public static string CacheFile(string language) =>
        Path.Combine(BaseDirectory, $"cache-{Sanitize(language)}.json");

    public static string EnsureBaseDirectory()
    {
        Directory.CreateDirectory(BaseDirectory);
        return BaseDirectory;
    }

    public static string EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        return LogDirectory;
    }

    private static string Sanitize(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(language.Trim().Where(c => !invalid.Contains(c)).ToArray());
        return cleaned.Length == 0 ? "unknown" : cleaned.ToLowerInvariant();
    }
}
