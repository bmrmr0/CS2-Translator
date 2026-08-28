using CS2.Translator.Core.Enums;
using CS2.Translator.Core.Parsing;
using Xunit;

namespace CS2.Translator.Core.Tests;

public class ChatLineParserTests
{
    private const string Marker = "﹫";

    [Fact]
    public void Parses_a_plain_chat_line()
    {
        Assert.True(ChatLineParser.TryParse($"Player{Marker} : hello world", out var chat));

        Assert.Equal("Player", chat.Name);
        Assert.Equal("hello world", chat.Message);
    }

    [Fact]
    public void Strips_the_clan_tag_marker_from_the_name()
    {
        Assert.True(ChatLineParser.TryParse($"Player{Marker}clan : hi", out var chat));

        Assert.Equal("Player", chat.Name);
        Assert.DoesNotContain(Marker, chat.Name);
    }

    [Fact]
    public void Keeps_a_colon_that_belongs_to_the_message()
    {
        Assert.True(ChatLineParser.TryParse($"Player{Marker} : score is 10: 4", out var chat));

        Assert.Equal("score is 10: 4", chat.Message);
    }

    [Fact]
    public void Does_not_let_a_name_containing_a_colon_swallow_the_message()
    {
        Assert.True(ChatLineParser.TryParse($"Weird: Name{Marker} : hi", out var chat));

        Assert.Equal("Weird: Name", chat.Name);
        Assert.Equal("hi", chat.Message);
    }

    [Theory]
    [InlineData("[ALL] Player{0} : gg", ChatType.All)]
    [InlineData("*DEAD* Player{0} : rip", ChatType.Dead)]
    [InlineData("(Counter-Terrorist) Player{0} : rush b", ChatType.Team)]
    [InlineData("(Terrorist) Player{0} : rush b", ChatType.Team)]
    [InlineData("(Spectator) Player{0} : nice", ChatType.Spectator)]
    [InlineData("Player{0} : plain", ChatType.Unknown)]
    public void Detects_the_chat_type(string template, ChatType expected)
    {
        var line = string.Format(template, Marker);

        Assert.True(ChatLineParser.TryParse(line, out var chat));
        Assert.Equal(expected, chat.ChatType);
        Assert.Equal("Player", chat.Name);
    }

    [Fact]
    public void Parses_the_bracket_tag_form_without_a_marker()
    {
        Assert.True(ChatLineParser.TryParse("01/02 03:04:05  [ALL] Player: hey", out var chat));

        Assert.Equal("Player", chat.Name);
        Assert.Equal("hey", chat.Message);
        Assert.Equal(ChatType.All, chat.ChatType);
    }

    [Theory]
    [InlineData("Loading map de_dust2")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ignores_lines_that_are_not_chat(string line)
    {
        Assert.False(ChatLineParser.TryParse(line, out _));
    }

    [Fact]
    public void Ignores_a_chat_line_with_an_empty_message()
    {
        Assert.False(ChatLineParser.TryParse($"Player{Marker} : ", out _));
    }

    [Fact]
    public void ParseLines_keeps_only_chat_lines_and_preserves_order()
    {
        var lines = new[]
        {
            "Loading map de_dust2",
            $"First{Marker} : one",
            "some other console noise",
            $"Second{Marker} : two"
        };

        var chats = ChatLineParser.ParseLines(lines);

        Assert.Equal(2, chats.Count);
        Assert.Equal("First", chats[0].Name);
        Assert.Equal("Second", chats[1].Name);
    }

    // The lines below are the shape CS2 actually writes with -condebug:
    //   "MM/DD HH:MM:SS  [T] NAME<U+200E>﹫LOCATION: message"
    // The location often contains spaces, and the name is followed by an invisible
    // left-to-right mark.
    private const string Ltr = "‎";

    [Fact]
    public void Parses_a_real_cs2_team_chat_line()
    {
        var line = $"08/28 21:30:03  [T] bmrmr{Ltr}{Marker}T Start: xaxaxaxaxa";

        Assert.True(ChatLineParser.TryParse(line, out var chat));

        Assert.Equal("bmrmr", chat.Name);
        Assert.Equal("xaxaxaxaxa", chat.Message);
        Assert.Equal(ChatType.Team, chat.ChatType);
    }

    [Theory]
    [InlineData("T Start")]
    [InlineData("Back Way")]
    [InlineData("Bombsite B")]
    [InlineData("CT Start")]
    [InlineData("Garage")]
    public void Does_not_treat_the_map_location_as_part_of_the_name(string location)
    {
        var line = $"08/28 22:43:07  [CT] SPRINT{Ltr}{Marker}{location}: hello";

        Assert.True(ChatLineParser.TryParse(line, out var chat));

        Assert.Equal("SPRINT", chat.Name);
        Assert.Equal("hello", chat.Message);
    }

    [Fact]
    public void Strips_the_invisible_left_to_right_mark_from_the_name()
    {
        var line = $"08/29 00:51:08  [CT] xenoformm{Ltr}{Marker}CT Start: TAKTIM ayaz";

        Assert.True(ChatLineParser.TryParse(line, out var chat));

        Assert.Equal("xenoformm", chat.Name);

        // Ordinal on purpose: a culture-sensitive search treats U+200E as zero-weight
        // and reports it as present in any string.
        Assert.False(chat.Name.Contains('‎'));
    }

    [Fact]
    public void Handles_non_latin_player_names()
    {
        var line = $"08/28 23:27:40  [CT] 當隱在清晨醒來時，大{Ltr}{Marker}Bombsite B: why";

        Assert.True(ChatLineParser.TryParse(line, out var chat));

        Assert.Equal("當隱在清晨醒來時，大", chat.Name);
        Assert.Equal("why", chat.Message);
    }

    [Fact]
    public void Collapses_whitespace_in_names()
    {
        Assert.True(ChatLineParser.TryParse($"  Spaced    Name  {Marker} : hi", out var chat));

        Assert.Equal("Spaced Name", chat.Name);
    }
}
