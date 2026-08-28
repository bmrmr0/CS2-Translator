using System.Text.Json;

namespace CS2.Translator.Core.Helper;

/// <summary>
/// Persistent source-text to translation map, one file per target language.
/// Writes are debounced and atomic: the previous version rewrote the whole file
/// on every single translated message, which was both slow and corruptible.
/// </summary>
public sealed class TranslationCache : IDisposable
{
    private const int MaxEntries = 3000;

    /// <summary>Trim in batches so we are not sorting the whole map on every insert once full.</summary>
    private const int TrimBatch = 300;

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly Timer _saveTimer;

    private bool _dirty;
    private bool _disposed;

    public TranslationCache(string language)
    {
        AppPaths.EnsureBaseDirectory();
        _filePath = AppPaths.CacheFile(language);

        Load();

        _saveTimer = new Timer(_ => SaveIfDirty(), null, SaveDelay, SaveDelay);
    }

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    public bool TryGet(string sourceText, out string translation)
    {
        var key = Normalize(sourceText);

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.LastUsed = DateTime.UtcNow;
                translation = entry.Translation;
                return true;
            }
        }

        translation = string.Empty;
        return false;
    }

    public void Set(string sourceText, string translation)
    {
        var key = Normalize(sourceText);
        if (key.Length == 0)
            return;

        lock (_lock)
        {
            _cache[key] = new CacheEntry
            {
                Translation = translation,
                LastUsed = DateTime.UtcNow
            };

            EnforceLimit();
            _dirty = true;
        }
    }

    /// <summary>Writes pending changes immediately. Called on shutdown.</summary>
    public void Flush() => SaveIfDirty();

    private void EnforceLimit()
    {
        if (_cache.Count <= MaxEntries)
            return;

        var removeCount = _cache.Count - MaxEntries + TrimBatch;

        var removeKeys = _cache
            .OrderBy(kv => kv.Value.LastUsed)
            .Take(removeCount)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in removeKeys)
            _cache.Remove(key);
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
            if (data is null)
                return;

            foreach (var kv in data)
            {
                if (kv.Value is not null)
                    _cache[kv.Key] = kv.Value;
            }

            DebugLogger.Log($"Loaded {_cache.Count} cached translations from {_filePath}", "Cache");
        }
        catch (Exception ex)
        {
            // A corrupt cache is not worth failing over - start fresh.
            DebugLogger.LogException(ex, "TranslationCache.Load");
            _cache.Clear();
        }
    }

    private void SaveIfDirty()
    {
        Dictionary<string, CacheEntry> snapshot;

        lock (_lock)
        {
            if (!_dirty)
                return;

            snapshot = new Dictionary<string, CacheEntry>(_cache);
            _dirty = false;
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot);
            var temp = _filePath + ".tmp";

            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugLogger.LogException(ex, "TranslationCache.Save");

            // Put the flag back so the next tick retries.
            lock (_lock)
                _dirty = true;
        }
    }

    /// <summary>
    /// Collapses runs of whitespace and trims. Combined with the case-insensitive
    /// comparer this folds together the many near-identical lines that show up in chat.
    /// </summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new System.Text.StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                    builder.Append(' ');

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _saveTimer.Dispose();
        SaveIfDirty();
    }

    private sealed class CacheEntry
    {
        public string Translation { get; set; } = "";
        public DateTime LastUsed { get; set; }
    }
}
