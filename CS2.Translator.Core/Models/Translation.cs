namespace CS2.Translator.Core.Models;

public sealed class Translation(string language, string text)
{
    public string Language { get; } = language;
    public string Text { get; } = text;

    public static Translation Empty(string language) => new(language, string.Empty);
}
