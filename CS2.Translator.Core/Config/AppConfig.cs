using System.Text.Json.Serialization;

namespace CS2.Translator.Core.Config;

public class AppConfig
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 32;
    public const double DefaultNameFontSize = 14;
    public const double DefaultTranslationFontSize = 12;

    public string InstallationPath { get; set; } = "";
    public string Language { get; set; } = "en";
    public string PlayerName { get; set; } = "";
    public double NameFontSize { get; set; } = DefaultNameFontSize;
    public double TranslationFontSize { get; set; } = DefaultTranslationFontSize;

    /// <summary>Translate incoming chat automatically. Off means messages are still listed, untranslated.</summary>
    public bool AutoTranslate { get; set; } = true;

    /// <summary>
    /// Show the untranslated message under the translation.
    /// </summary>
    public bool ShowOriginalMessage { get; set; } = true;

    /// <summary>
    /// Read console.log from the beginning on startup instead of tailing from the end.
    /// console.log accumulates across sessions, so leaving this off is what keeps the
    /// first few seconds from firing hundreds of translation requests and tripping the rate limit.
    /// </summary>
    public bool TranslateHistoryOnStartup { get; set; }

    /// <summary>How many chat entries to keep in memory and on screen.</summary>
    public int MaxChats { get; set; } = 150;

    public void Validate()
    {
        Language = string.IsNullOrWhiteSpace(Language) ? "en" : Language.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(InstallationPath))
            InstallationPath = GetDefaultCsPath();
        else
            InstallationPath = InstallationPath.Trim();

        PlayerName = PlayerName?.Trim() ?? "";

        NameFontSize = ClampFont(NameFontSize, DefaultNameFontSize);
        TranslationFontSize = ClampFont(TranslationFontSize, DefaultTranslationFontSize);

        MaxChats = Math.Clamp(MaxChats <= 0 ? 150 : MaxChats, 20, 2000);
    }

    private static double ClampFont(double value, double fallback)
    {
        if (double.IsNaN(value) || value <= 0)
            return fallback;

        return Math.Clamp(Math.Round(value), MinFontSize, MaxFontSize);
    }

    /// <summary>Full path to the console.log that <c>-condebug</c> writes. Derived, so it is not persisted.</summary>
    [JsonIgnore]
    public string ConsoleLogPath => ResolveConsoleLogPath(InstallationPath);

    public static string ResolveConsoleLogPath(string installationPath) =>
        Path.Combine(installationPath, "game", "csgo", "console.log");

    private static string GetDefaultCsPath()
    {
        foreach (var candidate in CandidateCsPaths())
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return CandidateCsPaths().First();
    }

    private static IEnumerable<string> CandidateCsPaths()
    {
        foreach (var steamRoot in CandidateSteamRoots())
            yield return Path.Combine(steamRoot, "steamapps", "common", "Counter-Strike Global Offensive");
    }

    private static IEnumerable<string> CandidateSteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(ProgramFilesX86(), "Steam");
            yield return Path.Combine(ProgramFiles(), "Steam");

            // Steam library folders on other drives are the common case once C: fills up.
            foreach (var drive in "DEFGH")
            {
                yield return $"{drive}:{Path.DirectorySeparatorChar}Steam";
                yield return $"{drive}:{Path.DirectorySeparatorChar}SteamLibrary";
            }

            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    private static string ProgramFilesX86() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    private static string ProgramFiles() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
}
