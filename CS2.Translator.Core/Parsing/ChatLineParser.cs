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

    // Only real chat tags qualify a line. The previous gate accepted any "[word]" after two
    // spaces, which matched ordinary console output such as
    //   "[Entity System] Entity  [light_rect]: unrecognized parent" - 162 of those in a
    // single 2MB log, all of which were shown as chat messages.
    [GeneratedRegex(@"\[\s*(ALL|T|CT|TEAM|DEAD|SPEC|SPECTATOR)\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatTag();

    [GeneratedRegex(@"\d{1,2}/\d{1,2}\s+\d{1,2}:\d{1,2}:\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

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

    /// <summary>
    /// CS2 writes chat in two shapes, both prefixed with a timestamp:
    ///   all chat   "[ALL] NAME&lt;U+200E&gt;: message"   (optionally " [DEAD]" after the name)
    ///   team chat  "[T] NAME&lt;U+200E&gt;﹫LOCATION: message"
    /// Team chat carries the sender's position on the map; all chat does not.
    /// </summary>
    public static bool TryParse(string line, out Chat chat)
    {
        chat = null!;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var separator = FindSeparator(line);
        if (separator < 0)
            return false;

        var rawName = line[..separator];

        // Everything from the marker onwards is the sender's map location, not their name.
        var markerIndex = rawName.IndexOf(ClanTagMarker);
        var namePart = markerIndex >= 0 ? rawName[..markerIndex] : rawName;

        // Gate on the name side only, so a "[T]" typed into a message cannot qualify a line.
        if (markerIndex < 0 && !ChatTag().IsMatch(namePart))
            return false;

        var message = line[(separator + Separator.Length)..].Trim();
        if (message.Length == 0)
            return false;

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

    /// <summary>
    /// A line can carry more than one tag, e.g. "[ALL] name [DEAD]". The most specific
    /// state wins, because "dead" tells the reader more than "all chat" does.
    /// </summary>
    private static ChatType DetectChatType(string namePart)
    {
        if (DeadPrefix().IsMatch(namePart))
            return ChatType.Dead;

        var team = TeamPrefix().Match(namePart);
        if (team.Success)
        {
            return team.Groups[1].Value.Equals("Spectator", StringComparison.OrdinalIgnoreCase)
                ? ChatType.Spectator
                : ChatType.Team;
        }

        var result = ChatType.Unknown;

        foreach (Match tag in ChatTag().Matches(namePart))
        {
            switch (tag.Groups[1].Value.ToUpperInvariant())
            {
                case "DEAD":
                    return ChatType.Dead;
                case "SPEC":
                case "SPECTATOR":
                    return ChatType.Spectator;
                case "T":
                case "CT":
                case "TEAM":
                    result = ChatType.Team;
                    break;
                case "ALL":
                    if (result == ChatType.Unknown)
                        result = ChatType.All;
                    break;
            }
        }

        return result;
    }

    private static string CleanName(string namePart)
    {
        var name = Timestamp().Replace(namePart, "");
        name = DeadPrefix().Replace(name, "");
        name = TeamPrefix().Replace(name, "");
        // Only known chat tags are stripped, so a player called "[NoSkill]bob" keeps their name.
        name = ChatTag().Replace(name, "");
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
