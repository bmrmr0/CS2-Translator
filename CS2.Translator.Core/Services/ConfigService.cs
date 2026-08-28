using System.Text.Json;
using CS2.Translator.Core.Config;
using CS2.Translator.Core.Helper;

namespace CS2.Translator.Core.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public AppConfig Config { get; private set; } = new();

    public string ConfigPath => AppPaths.ConfigFile;

    /// <summary>Raised after <see cref="Save"/> so live services can pick the new settings up.</summary>
    public event Action? ConfigChanged;

    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            Config.Validate();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "ConfigService.Load");
            Config = new AppConfig();
        }

        Config.Validate();
    }

    public void Save()
    {
        Config.Validate();

        try
        {
            AppPaths.EnsureBaseDirectory();

            var json = JsonSerializer.Serialize(Config, SerializerOptions);
            var temp = ConfigPath + ".tmp";

            File.WriteAllText(temp, json);
            File.Move(temp, ConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "ConfigService.Save");
        }

        ConfigChanged?.Invoke();
    }
}
