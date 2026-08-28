using System.Globalization;
using System.Text.RegularExpressions;
using CS2.Translator.Core.Enums;
using CS2.Translator.Core.Models;

namespace CS2.Translator.Core.Parsing;

/// <summary>
/// Turns raw console.log lines into <see cref="Chat"/> entries.
/// Kept free of I/O and state so it can be unit tested against real log samples.
/// </summary>
public static partial class ChatLineParser
{
    /// <summary>U+FE6B SMALL COMMERCIAL AT - CS2 writes this directly after the player name.</summary>
    public const char ClanTagMarker = '﹫';

    private const string Separator = ": ";

    // Same gate the service used before: two spaces followed by a [TAG], or the clan-tag marker.
    // Deliberately loose - community servers use tags we do not know about.
    [GeneratedRegex(@"\s\s\[\w+\]", RegexOptions.CultureInvariant)]
    private static partial Regex ChatLineGate();

    [GeneratedRegex(@"\d{1,2}/\d{1,2}\s+\d{1,2}:\d{1,2}:\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"\[(\w+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketTag();

    [GeneratedRegex(@"\*\s*DEAD\s*\*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeadPrefix();

    [GeneratedRegex(@"\((Counter-Terrorist|Terrorist|Spectator)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TeamPrefix();

    [GeneratedRegex(@"﹫\w*", RegexOptions.CultureInvariant)]
    private static partial Regex ClanTag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static List<Chat> ParseLines(IEnumerable<string> lines)
    {
        var chats = new List<Chat>();

        foreach (var line in lines)
        {
            if (TryParse(line, out var chat))
                chats.Add(chat);
        }

        return chats;
    }

    public static bool TryParse(string line, out Chat chat)
    {
        chat = null!;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (!line.Contains(ClanTagMarker) && !ChatLineGate().IsMatch(line))
            return false;

        var separator = FindSeparator(line);
        if (separator < 0)
            return false;

        var rawName = line[..separator];
        var message = line[(separator + Separator.Length)..].Trim();
        if (message.Length == 0)
            return false;

        // CS2 writes "NAME<U+200E>﹫LOCATION: message", so everything from the marker
        // onwards is the player's position on the map, not part of their name.
        var markerIndex = rawName.IndexOf(ClanTagMarker);
        var namePart = markerIndex >= 0 ? rawName[..markerIndex] : rawName;

        var name = CleanName(namePart);
        if (name.Length == 0)
            return false;

        chat = new Chat(line, DetectChatType(namePart), name, message);
        return true;
    }

    /// <summary>
    /// Splits on the first ": " that follows the clan-tag marker, so player names
    /// containing ": " do not swallow the message. Falls back to the first ": " on the line.
    /// </summary>
    private static int FindSeparator(string line)
    {
        var marker = line.IndexOf(ClanTagMarker);
        if (marker >= 0)
        {
            var afterMarker = line.IndexOf(Separator, marker, StringComparison.Ordinal);
            if (afterMarker >= 0)
                return afterMarker;
        }

        return line.IndexOf(Separator, StringComparison.Ordinal);
    }

    private static ChatType DetectChatType(string rawName)
    {
        if (DeadPrefix().IsMatch(rawName))
            return ChatType.Dead;

        var team = TeamPrefix().Match(rawName);
        if (team.Success)
        {
            return team.Groups[1].Value.Equals("Spectator", StringComparison.OrdinalIgnoreCase)
                ? ChatType.Spectator
                : ChatType.Team;
        }

        foreach (Match tag in BracketTag().Matches(rawName))
        {
            switch (tag.Groups[1].Value.ToUpperInvariant())
            {
                case "ALL":
                    return ChatType.All;
                case "T":
                case "CT":
                case "TEAM":
                    return ChatType.Team;
                case "DEAD":
                    return ChatType.Dead;
                case "SPEC":
                case "SPECTATOR":
                    return ChatType.Spectator;
            }
        }

        return ChatType.Unknown;
    }

    private static string CleanName(string namePart)
    {
        var name = Timestamp().Replace(namePart, "");
        name = DeadPrefix().Replace(name, "");
        name = TeamPrefix().Replace(name, "");
        name = BracketTag().Replace(name, "");
        name = ClanTag().Replace(name, "");
        name = RemoveFormatCharacters(name);

        return Whitespace().Replace(name, " ").Trim();
    }

    /// <summary>
    /// Drops invisible formatting characters. CS2 puts a U+200E left-to-right mark
    /// between the player name and the marker, which would otherwise survive into the name.
    /// </summary>
    private static string RemoveFormatCharacters(string text)
    {
        if (!text.Any(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
            return text;

        return string.Concat(
            text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format));
    }
}
