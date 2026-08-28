namespace CS2.Translator.Core.Exceptions;

public class TranslatorException(string message) : Exception(message);

public class NoInternetException() : TranslatorException("No internet connection");

/// <summary>
/// The provider answered 429, or kept failing long enough that we backed off.
/// <see cref="RetryAfter"/> is how long the service will refuse to send new requests.
/// </summary>
public class RateLimitedException(TimeSpan retryAfter)
    : TranslatorException($"Rate limited - pausing {retryAfter.TotalSeconds:F0}s")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public class GoogleTranslateTimeoutException() : TranslatorException("Google Translate did not respond in time");
