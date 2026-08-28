using CS2.Translator.Core.Config;
using Xunit;

namespace CS2.Translator.Core.Tests;

public class AppConfigTests
{
    [Theory]
    [InlineData(0, AppConfig.DefaultNameFontSize)]
    [InlineData(-5, AppConfig.DefaultNameFontSize)]
    [InlineData(4, AppConfig.MinFontSize)]
    [InlineData(100, AppConfig.MaxFontSize)]
    [InlineData(16, 16)]
    public void Clamps_the_name_font_size(double input, double expected)
    {
        var config = new AppConfig { NameFontSize = input };

        config.Validate();

        Assert.Equal(expected, config.NameFontSize);
    }

    [Theory]
    [InlineData("", "en")]
    [InlineData("   ", "en")]
    [InlineData(" DE ", "de")]
    [InlineData("zh-CN", "zh-cn")]
    public void Normalises_the_language(string input, string expected)
    {
        var config = new AppConfig { Language = input };

        config.Validate();

        Assert.Equal(expected, config.Language);
    }

    [Theory]
    [InlineData(0, 150)]
    [InlineData(-1, 150)]
    [InlineData(5, 20)]
    [InlineData(99999, 2000)]
    [InlineData(300, 300)]
    public void Clamps_the_chat_limit(int input, int expected)
    {
        var config = new AppConfig { MaxChats = input };

        config.Validate();

        Assert.Equal(expected, config.MaxChats);
    }

    [Fact]
    public void Fills_in_an_installation_path_when_none_is_set()
    {
        var config = new AppConfig { InstallationPath = "" };

        config.Validate();

        Assert.False(string.IsNullOrWhiteSpace(config.InstallationPath));
    }

    [Fact]
    public void Builds_the_console_log_path_under_game_csgo()
    {
        var config = new AppConfig { InstallationPath = Path.Combine("X:", "CS2") };

        config.Validate();

        Assert.EndsWith(Path.Combine("game", "csgo", "console.log"), config.ConsoleLogPath);
    }

    [Fact]
    public void Trims_the_player_name()
    {
        var config = new AppConfig { PlayerName = "  bmrmr  " };

        config.Validate();

        Assert.Equal("bmrmr", config.PlayerName);
    }
}
