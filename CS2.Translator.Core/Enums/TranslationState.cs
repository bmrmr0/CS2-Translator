namespace CS2.Translator.Core.Enums;

public enum TranslationState
{
    /// <summary>Queued or in flight - the UI shows a placeholder.</summary>
    Pending,

    /// <summary>Text came back from the provider or the cache.</summary>
    Translated,

    /// <summary>Deliberately not translated: own message, auto-translate off, or nothing translatable.</summary>
    Skipped,

    /// <summary>The provider failed. The translation text holds the reason.</summary>
    Failed
}
