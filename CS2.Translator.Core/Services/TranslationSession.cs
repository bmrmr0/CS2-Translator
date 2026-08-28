using CS2.Translator.Core.Exceptions;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Models;

namespace CS2.Translator.Core.Services;

/// <summary>
/// Owns the services that are built from configuration and can rebuild them in place.
/// Language, installation path and player name were previously baked in at window
/// construction, so changing them in Settings did nothing until the app was restarted.
/// </summary>
public sealed class TranslationSession : IDisposable
{
    private readonly ConfigService _configService;
    private readonly Action<Action> _post;
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    private TranslatorService? _translator;
    private LogsService? _logs;
    private bool _disposed;

    public event Action<Chat>? ChatReceived;
    public event Action? ChatsReset;
    public event Action<string>? StatusChanged;

    public TranslationSession(ConfigService configService, Action<Action>? post = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _post = post ?? (action => action());
    }

    public IReadOnlyList<Chat> Chats => _logs?.Chats ?? Array.Empty<Chat>();

    public string Status { get; private set; } = "Idle";

    public Task StartAsync() => RestartAsync();

    public async Task RestartAsync()
    {
        if (_disposed)
            return;

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            TearDown();

            var config = _configService.Config;

            _translator = new TranslatorService(config.Language);
            _translator.RateLimited += OnRateLimited;
            _translator.Recovered += OnRecovered;

            _logs = new LogsService(config, _translator, _post);
            _logs.ChatReceived += OnChatReceived;
            _logs.ChatsReset += OnChatsReset;

            SetStatus("Loading logs...");

            await _logs.StartAsync().ConfigureAwait(false);

            SetStatus(_logs.LogFileExists
                ? "Watching CS2 console.log"
                : "Waiting for CS2 - console.log not written yet (is -condebug set?)");
        }
        catch (LogfileNotFoundException)
        {
            TearDown();
            SetStatus("CS2 folder not found - check the installation path in Settings");
        }
        catch (ArgumentException ex)
        {
            TearDown();
            SetStatus($"Invalid configuration: {ex.Message}");
        }
        catch (Exception ex)
        {
            TearDown();
            DebugLogger.LogException(ex, "TranslationSession.RestartAsync");
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _restartGate.Release();
        }
    }

    public async Task ReloadAsync()
    {
        var logs = _logs;
        if (logs is null)
        {
            await RestartAsync().ConfigureAwait(false);
            return;
        }

        SetStatus("Reloading...");

        try
        {
            await logs.ReloadAsync().ConfigureAwait(false);
            SetStatus("Watching CS2 console.log");
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "TranslationSession.ReloadAsync");
            SetStatus($"Reload failed: {ex.Message}");
        }
    }

    private void OnChatReceived(Chat chat) => ChatReceived?.Invoke(chat);

    private void OnChatsReset() => ChatsReset?.Invoke();

    private void OnRateLimited(TimeSpan cooldown) =>
        SetStatus($"Rate limited by Google - pausing translation for {cooldown.TotalSeconds:F0}s");

    private void OnRecovered() => SetStatus("Watching CS2 console.log");

    private void SetStatus(string status)
    {
        Status = status;
        _post(() => StatusChanged?.Invoke(status));
    }

    private void TearDown()
    {
        if (_logs is not null)
        {
            _logs.ChatReceived -= OnChatReceived;
            _logs.ChatsReset -= OnChatsReset;
            _logs.Dispose();
            _logs = null;
        }

        if (_translator is not null)
        {
            _translator.RateLimited -= OnRateLimited;
            _translator.Recovered -= OnRecovered;
            _translator.Dispose();
            _translator = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        TearDown();
        _restartGate.Dispose();
    }
}
