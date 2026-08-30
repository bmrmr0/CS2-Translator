using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2.Translator.Core.Config;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Services;

namespace CS2.Translator.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    /// <summary>Supplied by the window so the view model does not depend on the storage provider.</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    [ObservableProperty]
    private string _installationPath = "";

    [ObservableProperty]
    private string _language = "en";

    [ObservableProperty]
    private string _playerName = "";

    [ObservableProperty]
    private double _nameFontSize;

    [ObservableProperty]
    private double _translationFontSize;

    [ObservableProperty]
    private bool _autoTranslate;

    [ObservableProperty]
    private bool _showOriginalMessage;

    [ObservableProperty]
    private bool _translateHistoryOnStartup;

    [ObservableProperty]
    private bool _showTeamIndicator;

    [ObservableProperty]
    private bool _showDeadIndicator;

    [ObservableProperty]
    private string _pathStatus = "";

    [ObservableProperty]
    private bool _pathIsValid;

    public double MinFontSize => AppConfig.MinFontSize;
    public double MaxFontSize => AppConfig.MaxFontSize;

    /// <summary>Common targets for the language box. Any Google Translate code still works.</summary>
    public IReadOnlyList<string> CommonLanguages { get; } = new[]
    {
        "ar", "cs", "da", "de", "el", "en", "es", "fi", "fr", "he", "hi", "hu",
        "id", "it", "ja", "ko", "nl", "no", "pl", "pt", "ro", "ru", "sv", "th",
        "tr", "uk", "vi", "zh-CN", "zh-TW"
    };

    public event Action? CloseRequested;

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;

        var config = configService.Config;

        _installationPath = config.InstallationPath;
        _language = config.Language;
        _playerName = config.PlayerName;
        _nameFontSize = config.NameFontSize;
        _translationFontSize = config.TranslationFontSize;
        _autoTranslate = config.AutoTranslate;
        _showOriginalMessage = config.ShowOriginalMessage;
        _translateHistoryOnStartup = config.TranslateHistoryOnStartup;
        _showTeamIndicator = config.ShowTeamIndicator;
        _showDeadIndicator = config.ShowDeadIndicator;

        UpdatePathStatus();
    }

    partial void OnInstallationPathChanged(string value) => UpdatePathStatus();

    /// <summary>
    /// Tells the user up front whether the path is usable, instead of letting them
    /// save a wrong one and wonder why nothing appears.
    /// </summary>
    private void UpdatePathStatus()
    {
        if (string.IsNullOrWhiteSpace(InstallationPath))
        {
            PathIsValid = false;
            PathStatus = "Enter the folder that contains game/csgo.";
            return;
        }

        var logPath = AppConfig.ResolveConsoleLogPath(InstallationPath.Trim());
        var logDirectory = Path.GetDirectoryName(logPath);

        if (logDirectory is null || !Directory.Exists(logDirectory))
        {
            PathIsValid = false;
            PathStatus = "Not a CS2 install - expected a game/csgo folder inside this path.";
            return;
        }

        PathIsValid = true;

        PathStatus = File.Exists(logPath)
            ? "console.log found."
            : "Folder looks right, but console.log is missing. Add -condebug to the CS2 launch options and start the game.";
    }

    [RelayCommand]
    private async Task Browse()
    {
        if (PickFolderAsync is null)
            return;

        try
        {
            var picked = await PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(picked))
                InstallationPath = picked;
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "SettingsViewModel.Browse");
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => DebugLogger.OpenLogFolder();

    [RelayCommand]
    private void Save()
    {
        var config = _configService.Config;

        config.InstallationPath = InstallationPath;
        config.Language = Language;
        config.PlayerName = PlayerName;
        config.NameFontSize = NameFontSize;
        config.TranslationFontSize = TranslationFontSize;
        config.AutoTranslate = AutoTranslate;
        config.ShowOriginalMessage = ShowOriginalMessage;
        config.TranslateHistoryOnStartup = TranslateHistoryOnStartup;
        config.ShowTeamIndicator = ShowTeamIndicator;
        config.ShowDeadIndicator = ShowDeadIndicator;

        // Save validates and clamps, then notifies listeners so the session restarts.
        _configService.Save();

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
