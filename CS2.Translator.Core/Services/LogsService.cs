using System.Threading.Channels;
using CS2.Translator.Core.Config;
using CS2.Translator.Core.Enums;
using CS2.Translator.Core.Exceptions;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Models;
using CS2.Translator.Core.Parsing;

namespace CS2.Translator.Core.Services;

/// <summary>
/// Tails the CS2 console.log, turns appended lines into <see cref="Chat"/> entries and
/// hands them to the translator. Reads are positional and serialised; translation happens
/// on a background queue so a slow or throttled provider never stalls the log reader.
/// </summary>
public sealed class LogsService : IDisposable
{
    /// <summary>How much of the tail to show as backlog when the app starts.</summary>
    private const int BacklogWindowBytes = 64 * 1024;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly string _logFilePath;
    private readonly string _logDirectory;
    private readonly TranslatorService _translator;
    private readonly string _targetLanguage;
    private readonly string _playerName;
    private readonly bool _autoTranslate;
    private readonly bool _translateHistoryOnStartup;
    private readonly int _maxChats;
    private readonly Action<Action> _post;

    private readonly List<Chat> _chats = new();
    private readonly object _chatsLock = new();

    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Channel<Chat> _queue = Channel.CreateUnbounded<Chat>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _lifetime = new();

    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private Task? _translationLoop;
    private CancellationTokenSource? _debounceCts;

    private long _lastFilePosition;
    private DateTime _lastWriteTimeUtc = DateTime.MinValue;
    private bool _disposed;

    /// <summary>Raised on the dispatcher thread for every chat entry, before it is translated.</summary>
    public event Action<Chat>? ChatReceived;

    /// <summary>Raised on the dispatcher thread when the backlog is rebuilt and the list should be replaced.</summary>
    public event Action? ChatsReset;

    public LogsService(
        AppConfig config,
        TranslatorService translator,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.InstallationPath))
            throw new ArgumentException("CS2 installation path is empty", nameof(config));

        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _logFilePath = config.ConsoleLogPath;
        _logDirectory = Path.GetDirectoryName(_logFilePath)
                        ?? throw new ArgumentException("CS2 installation path is not a valid directory", nameof(config));

        _targetLanguage = config.Language;
        _playerName = config.PlayerName;
        _autoTranslate = config.AutoTranslate;
        _translateHistoryOnStartup = config.TranslateHistoryOnStartup;
        _maxChats = config.MaxChats;

        // Runs continuations on the UI thread when the host supplies a dispatcher.
        _post = post ?? (action => action());

        DebugLogger.Log($"Watching {_logFilePath} (lang={_targetLanguage}, player='{_playerName}', autoTranslate={_autoTranslate})", "Logs");
    }

    public string LogFilePath => _logFilePath;

    public IReadOnlyList<Chat> Chats
    {
        get
        {
            lock (_chatsLock)
                return _chats.ToArray();
        }
    }

    /// <summary>True once the console.log exists. False means CS2 has not written it yet.</summary>
    public bool LogFileExists => File.Exists(_logFilePath);

    /// <summary>
    /// Seeds the backlog from the tail of the file, then starts watching for appended lines.
    /// Throws only when the installation path itself is wrong - a missing console.log is
    /// expected before CS2 has run with -condebug, and is picked up by the poll timer.
    /// </summary>
    public async Task StartAsync()
    {
        if (!Directory.Exists(_logDirectory))
            throw new LogfileNotFoundException();

        _translationLoop ??= Task.Run(() => TranslationLoopAsync(_lifetime.Token));

        await SeedBacklogAsync().ConfigureAwait(false);

        StartWatching();
    }

    /// <summary>Rebuilds the visible list from the tail of the file without re-translating it.</summary>
    public Task ReloadAsync() => SeedBacklogAsync();

    private void StartWatching()
    {
        if (_watcher is not null)
            return;

        _watcher = new FileSystemWatcher(_logDirectory, Path.GetFileName(_logFilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += (_, _) => RequestReload();
        _watcher.Created += (_, _) => RequestReset();
        _watcher.Renamed += (_, _) => RequestReset();
        _watcher.Deleted += (_, _) => RequestReset();
        _watcher.Error += (_, e) => DebugLogger.LogException(e.GetException(), "FileSystemWatcher");
        _watcher.EnableRaisingEvents = true;

        // The watcher misses writes on some setups (network shares, Proton, buffered writers),
        // so a cheap poll backs it up.
        _pollTimer = new Timer(_ => PollLogFile(), null, PollInterval, PollInterval);

        DebugLogger.Log("Watcher and poll timer started", "Logs");
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;

        _pollTimer?.Dispose();
        _pollTimer = null;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    private void PollLogFile()
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return;

            var info = new FileInfo(_logFilePath);

            if (info.Length < _lastFilePosition)
            {
                DebugLogger.Log("File shrank - treating as a new match log", "Logs");
                RequestReset();
                return;
            }

            if (info.Length > _lastFilePosition || info.LastWriteTimeUtc != _lastWriteTimeUtc)
                RequestReload();
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "PollLogFile");
        }
    }

    private void RequestReload() => _ = DebouncedAsync(ReadNewLinesAsync);

    private void RequestReset() => _ = DebouncedAsync(SeedBacklogAsync);

    private async Task DebouncedAsync(Func<Task> work)
    {
        var cts = new CancellationTokenSource();

        var previous = Interlocked.Exchange(ref _debounceCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(DebounceDelay, cts.Token).ConfigureAwait(false);
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer event.
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "DebouncedAsync");
        }
    }

    /// <summary>
    /// Replaces the visible list with what the tail of the file holds, and parks the read
    /// position at EOF. Backlog entries are not queued for translation unless the user asked
    /// for it - console.log accumulates across sessions, and translating all of it on startup
    /// is what used to trip the rate limit within seconds.
    /// </summary>
    private async Task SeedBacklogAsync()
    {
        await _readGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            List<string> lines;

            try
            {
                lines = await Task.Run(ReadTailWindow, _lifetime.Token).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                lines = new List<string>();
            }

            var parsed = ChatLineParser.ParseLines(lines);
            if (parsed.Count > _maxChats)
                parsed = parsed.GetRange(parsed.Count - _maxChats, _maxChats);

            foreach (var chat in parsed)
            {
                chat.IsOwnMessage = IsOwnMessage(chat.Name);
                chat.State = _translateHistoryOnStartup && ShouldTranslate(chat)
                    ? TranslationState.Pending
                    : TranslationState.Skipped;
            }

            lock (_chatsLock)
            {
                _chats.Clear();
                // Newest first, matching how the list is displayed.
                for (var i = parsed.Count - 1; i >= 0; i--)
                    _chats.Add(parsed[i]);
            }

            _post(() => ChatsReset?.Invoke());

            if (_translateHistoryOnStartup)
            {
                foreach (var chat in parsed.Where(c => c.State == TranslationState.Pending))
                    _queue.Writer.TryWrite(chat);
            }

            DebugLogger.Log($"Backlog seeded with {parsed.Count} entries, reading from offset {_lastFilePosition}", "Logs");
        }
        finally
        {
            _readGate.Release();
        }
    }

    private async Task ReadNewLinesAsync()
    {
        await _readGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        try
        {
            List<string> lines;

            try
            {
                lines = await Task.Run(ReadAppendedLines, _lifetime.Token).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return;
            }

            if (lines.Count == 0)
                return;

            var parsed = ChatLineParser.ParseLines(lines);
            if (parsed.Count == 0)
                return;

            foreach (var chat in parsed)
            {
                chat.IsOwnMessage = IsOwnMessage(chat.Name);

                if (ShouldTranslate(chat))
                {
                    chat.State = TranslationState.Pending;
                    _queue.Writer.TryWrite(chat);
                }
                else
                {
                    chat.State = TranslationState.Skipped;
                }

                lock (_chatsLock)
                {
                    _chats.Insert(0, chat);
                    TrimLocked();
                }

                // Surface the message immediately. The translation lands later via
                // property change notification, so a slow provider never delays display.
                var captured = chat;
                _post(() => ChatReceived?.Invoke(captured));
            }
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void TrimLocked()
    {
        // Oldest entries live at the end of the list.
        while (_chats.Count > _maxChats)
            _chats.RemoveAt(_chats.Count - 1);
    }

    private bool IsOwnMessage(string name) =>
        !string.IsNullOrEmpty(_playerName)
        && name.Equals(_playerName, StringComparison.OrdinalIgnoreCase);

    private bool ShouldTranslate(Chat chat) =>
        _autoTranslate
        && !chat.IsOwnMessage
        && TranslatorService.IsTranslatable(chat.Message);

    private async Task TranslationLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var chat in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                Translation translation;
                TranslationState state;

                try
                {
                    translation = await _translator.TranslateAsync(chat.Message, _targetLanguage, ct).ConfigureAwait(false);
                    state = TranslationState.Translated;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TranslatorException ex)
                {
                    translation = new Translation(_targetLanguage, ex.Message);
                    state = TranslationState.Failed;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogException(ex, "TranslationLoop");
                    translation = new Translation(_targetLanguage, "Translation failed");
                    state = TranslationState.Failed;
                }

                var captured = chat;
                _post(() => captured.Complete(translation, state));
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "TranslationLoopAsync");
        }
    }

    /// <summary>Reads the last <see cref="BacklogWindowBytes"/> of the file and parks the offset at EOF.</summary>
    private List<string> ReadTailWindow()
    {
        var result = new List<string>();

        if (!File.Exists(_logFilePath))
        {
            _lastFilePosition = 0;
            _lastWriteTimeUtc = DateTime.MinValue;
            throw new FileNotFoundException("console.log not found", _logFilePath);
        }

        using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var start = Math.Max(0, fs.Length - BacklogWindowBytes);
        fs.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(fs);

        // A mid-file seek almost certainly lands inside a line; drop that fragment.
        if (start > 0)
            reader.ReadLine();

        while (reader.ReadLine() is { } line)
            result.Add(line);

        _lastFilePosition = fs.Length;
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_logFilePath);

        return result;
    }

    private List<string> ReadAppendedLines()
    {
        var result = new List<string>();

        if (!File.Exists(_logFilePath))
            throw new FileNotFoundException("console.log not found", _logFilePath);

        using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (fs.Length < _lastFilePosition)
        {
            // Truncated or replaced between poll and read.
            _lastFilePosition = 0;
        }

        fs.Seek(_lastFilePosition, SeekOrigin.Begin);

        using var reader = new StreamReader(fs);
        while (reader.ReadLine() is { } line)
            result.Add(line);

        _lastFilePosition = fs.Length;
        _lastWriteTimeUtc = File.GetLastWriteTimeUtc(_logFilePath);

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        StopWatching();

        _queue.Writer.TryComplete();
        _lifetime.Cancel();

        try
        {
            _translationLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "LogsService.Dispose");
        }

        _lifetime.Dispose();
        _readGate.Dispose();

        DebugLogger.Log("Stopped", "Logs");
    }
}
