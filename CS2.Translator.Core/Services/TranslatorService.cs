using System.Net;
using System.Text.Json;
using CS2.Translator.Core.Exceptions;
using CS2.Translator.Core.Helper;
using CS2.Translator.Core.Models;

namespace CS2.Translator.Core.Services;

/// <summary>
/// Google Translate front end with a persistent cache, request pacing, retries with
/// backoff, and a cooldown that stops us hammering the endpoint once it starts
/// answering 429. The free endpoint throttles per IP, so pacing is what keeps it usable.
/// </summary>
public sealed class TranslatorService : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Minimum spacing between outbound requests, so a chat burst is not sent as a burst of calls.</summary>
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(350);

    private static readonly TimeSpan BaseCooldown = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);

    private const int MaxAttempts = 3;

    /// <summary>The query travels in the URL, so very long lines would come back as 414.</summary>
    private const int MaxCharsPerRequest = 900;

    // Shared so rebuilding the service on a settings change does not leak sockets.
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(5)
    };

    private readonly HttpClient _http;
    private readonly TranslationCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;
    private DateTime _cooldownUntilUtc = DateTime.MinValue;
    private int _rateLimitStrikes;
    private bool _disposed;

    /// <summary>Raised when the provider starts refusing us, with how long we intend to wait.</summary>
    public event Action<TimeSpan>? RateLimited;

    /// <summary>Raised on the first success after a cooldown.</summary>
    public event Action? Recovered;

    public TranslatorService(string targetLanguage)
    {
        _cache = new TranslationCache(targetLanguage);

        _http = new HttpClient(SharedHandler, disposeHandler: false)
        {
            Timeout = RequestTimeout
        };

        // A default-looking client is less likely to be singled out than a custom agent string.
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        DebugLogger.Log($"Initialized for target '{targetLanguage}' with {_cache.Count} cached entries", "Translator");
    }

    public bool IsInCooldown => DateTime.UtcNow < _cooldownUntilUtc;

    public TimeSpan CooldownRemaining
    {
        get
        {
            var remaining = _cooldownUntilUtc - DateTime.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Messages with no letters at all (scoreboard spam, punctuation, bare numbers)
    /// never need a round trip.
    /// </summary>
    public static bool IsTranslatable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetter);

    public async Task<Translation> TranslateAsync(string sourceText, string targetLang, CancellationToken ct = default)
    {
        if (!IsTranslatable(sourceText))
            return new Translation(targetLang, sourceText ?? string.Empty);

        if (_cache.TryGet(sourceText, out var cached))
            return new Translation(targetLang, cached);

        var remaining = CooldownRemaining;
        if (remaining > TimeSpan.Zero)
            throw new RateLimitedException(remaining);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have filled the cache or tripped a cooldown while we queued.
            if (_cache.TryGet(sourceText, out cached))
                return new Translation(targetLang, cached);

            remaining = CooldownRemaining;
            if (remaining > TimeSpan.Zero)
                throw new RateLimitedException(remaining);

            await PaceAsync(ct).ConfigureAwait(false);

            var translated = await TranslateWithRetriesAsync(Truncate(sourceText), targetLang, ct).ConfigureAwait(false);

            if (_rateLimitStrikes > 0)
            {
                _rateLimitStrikes = 0;
                Recovered?.Invoke();
            }

            _cache.Set(sourceText, translated);
            return new Translation(targetLang, translated);
        }
        finally
        {
            _nextAllowedRequestUtc = DateTime.UtcNow + MinRequestInterval;
            _gate.Release();
        }
    }

    private async Task PaceAsync(CancellationToken ct)
    {
        var wait = _nextAllowedRequestUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct).ConfigureAwait(false);
    }

    private static string Truncate(string text) =>
        text.Length <= MaxCharsPerRequest ? text : text[..MaxCharsPerRequest];

    private async Task<string> TranslateWithRetriesAsync(string text, string lang, CancellationToken ct)
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Alternate endpoints across attempts - when one is throttling, the other often is not.
            var url = attempt % 2 == 1 ? PrimaryUrl(text, lang) : FallbackUrl(text, lang);

            try
            {
                var json = await SendAsync(url, ct).ConfigureAwait(false);
                var result = ParseResponse(json);

                if (!string.IsNullOrWhiteSpace(result))
                    return result;

                last = new TranslatorException("Empty response from translate endpoint");
            }
            catch (ThrottledSignal signal)
            {
                last = signal;

                if (attempt == MaxAttempts)
                    throw EnterCooldown(signal.RetryAfter);

                await BackoffAsync(attempt, signal.RetryAfter, ct).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                // Not cancelled by the caller, so this is the HttpClient timeout firing.
                last = new GoogleTranslateTimeoutException();
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }

            if (attempt < MaxAttempts)
                await BackoffAsync(attempt, null, ct).ConfigureAwait(false);
        }

        DebugLogger.Log($"All {MaxAttempts} attempts failed: {last?.Message}", "Translator");

        throw last switch
        {
            GoogleTranslateTimeoutException timeout => timeout,
            HttpRequestException => new NoInternetException(),
            TranslatorException translator => translator,
            _ => new TranslatorException(last?.Message ?? "Translation failed")
        };
    }

    private static Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken ct)
    {
        if (retryAfter is { } hinted && hinted > TimeSpan.Zero)
        {
            var capped = hinted < TimeSpan.FromSeconds(10) ? hinted : TimeSpan.FromSeconds(10);
            return Task.Delay(capped, ct);
        }

        // 500ms then 1500ms, plus jitter so retries do not line up.
        var delayMs = 500 * Math.Pow(3, attempt - 1) + Random.Shared.Next(0, 250);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
    }

    private RateLimitedException EnterCooldown(TimeSpan? retryAfter)
    {
        _rateLimitStrikes++;

        var factor = (long)Math.Pow(2, Math.Min(_rateLimitStrikes - 1, 4));
        var cooldown = TimeSpan.FromTicks(BaseCooldown.Ticks * factor);

        if (retryAfter is { } hinted && hinted > cooldown)
            cooldown = hinted;

        if (cooldown > MaxCooldown)
            cooldown = MaxCooldown;

        _cooldownUntilUtc = DateTime.UtcNow + cooldown;

        DebugLogger.Log($"Rate limited (strike {_rateLimitStrikes}) - pausing {cooldown.TotalSeconds:F0}s", "Translator");
        RateLimited?.Invoke(cooldown);

        return new RateLimitedException(cooldown);
    }

    private async Task<string> SendAsync(Uri url, CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (IsThrottleStatus(response.StatusCode))
            throw new ThrottledSignal(response.Headers.RetryAfter?.Delta);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static bool IsThrottleStatus(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.Forbidden
            or HttpStatusCode.ServiceUnavailable;

    private static Uri PrimaryUrl(string text, string lang) => new(
        "https://translate.googleapis.com/translate_a/single" +
        $"?client=gtx&sl=auto&tl={Uri.EscapeDataString(lang)}&dt=t&q={Uri.EscapeDataString(text)}");

    private static Uri FallbackUrl(string text, string lang) => new(
        "https://clients5.google.com/translate_a/t" +
        $"?client=dict-chrome-ex&sl=auto&tl={Uri.EscapeDataString(lang)}&q={Uri.EscapeDataString(text)}");

    /// <summary>
    /// Handles both response shapes we ask for: the gtx endpoint returns
    /// [[[chunk, source, ...], ...], ...] and dict-chrome-ex returns [[text, lang]].
    /// </summary>
    internal static string ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.String)
            return root.GetString() ?? string.Empty;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return string.Empty;

        var first = root[0];

        if (first.ValueKind == JsonValueKind.String)
            return first.GetString() ?? string.Empty;

        if (first.ValueKind != JsonValueKind.Array || first.GetArrayLength() == 0)
            return string.Empty;

        // dict-chrome-ex shape: the inner element is the translated string itself.
        if (first[0].ValueKind == JsonValueKind.String)
            return first[0].GetString() ?? string.Empty;

        var parts = new List<string>();
        foreach (var segment in first.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                continue;

            if (segment[0].ValueKind != JsonValueKind.String)
                continue;

            var part = segment[0].GetString();
            if (!string.IsNullOrEmpty(part))
                parts.Add(part);
        }

        return string.Concat(parts);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cache.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }

    /// <summary>Internal marker so the retry loop can tell throttling apart from other failures.</summary>
    private sealed class ThrottledSignal(TimeSpan? retryAfter) : Exception("Throttled by provider")
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
