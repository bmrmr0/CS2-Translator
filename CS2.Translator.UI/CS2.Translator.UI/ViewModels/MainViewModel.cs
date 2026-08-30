using System;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Models;
using CS2.Translator.Core.Services;

namespace CS2.Translator.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly TranslationSession _session;
    private readonly ConfigService _configService;

    private bool _disposed;

    /// <summary>
    /// Holds the live Chat instances from the service. They must not be copied:
    /// Chat raises PropertyChanged when its translation arrives, and a clone would
    /// never receive it.
    /// </summary>
    public AvaloniaList<Chat> Chats { get; } = new();

    [ObservableProperty]
    private string _statusText = "Idle";

    public event Action? SettingsRequested;

    public double NameFontSize => _configService.Config.NameFontSize;
    public double TranslationFontSize => _configService.Config.TranslationFontSize;
    public bool ShowOriginalMessage => _configService.Config.ShowOriginalMessage;
    public bool ShowTeamIndicator => _configService.Config.ShowTeamIndicator;
    public bool ShowDeadIndicator => _configService.Config.ShowDeadIndicator;

    private int MaxChats => _configService.Config.MaxChats;

    public MainViewModel(TranslationSession session, ConfigService configService)
    {
        _session = session;
        _configService = configService;

        _session.ChatReceived += OnChatReceived;
        _session.ChatsReset += OnChatsReset;
        _session.StatusChanged += OnStatusChanged;
        _configService.ConfigChanged += OnConfigChanged;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _session.StartAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            DebugLogger.LogException(ex, "MainViewModel.InitializeAsync");
        }
    }

    // All three handlers are invoked through the dispatcher the session was given,
    // so they are already on the UI thread.
    private void OnStatusChanged(string status) => StatusText = status;

    private void OnChatReceived(Chat chat)
    {
        Chats.Insert(0, chat);
        Trim();
    }

    private void OnChatsReset()
    {
        Chats.Clear();
        foreach (var chat in _session.Chats)
            Chats.Add(chat);

        Trim();
    }

    private void OnConfigChanged()
    {
        OnPropertyChanged(nameof(NameFontSize));
        OnPropertyChanged(nameof(TranslationFontSize));
        OnPropertyChanged(nameof(ShowOriginalMessage));
        OnPropertyChanged(nameof(ShowTeamIndicator));
        OnPropertyChanged(nameof(ShowDeadIndicator));

        // Language, install path and player name are baked into the services,
        // so they only take effect once those are rebuilt.
        _ = _session.RestartAsync();
    }

    /// <summary>Newest entries sit at index 0, so the oldest are trimmed off the end.</summary>
    private void Trim()
    {
        while (Chats.Count > MaxChats)
            Chats.RemoveAt(Chats.Count - 1);
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();

    [RelayCommand]
    private async Task Reload() => await _session.ReloadAsync();

    [RelayCommand]
    private void Clear() => Chats.Clear();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _session.ChatReceived -= OnChatReceived;
        _session.ChatsReset -= OnChatsReset;
        _session.StatusChanged -= OnStatusChanged;
        _configService.ConfigChanged -= OnConfigChanged;
    }
}
