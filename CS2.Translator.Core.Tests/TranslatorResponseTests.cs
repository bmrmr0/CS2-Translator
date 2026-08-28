using CS2.Translator.Core.Services;
using Xunit;

namespace CS2.Translator.Core.Tests;

public class TranslatorResponseTests
{
    [Fact]
    public void Reads_a_single_chunk_gtx_response()
    {
        const string json = """[[["Hallo","Hello",null,null,10]],null,"en"]""";

        Assert.Equal("Hallo", TranslatorService.ParseResponse(json));
    }

    [Fact]
    public void Joins_every_chunk_of_a_multi_part_gtx_response()
    {
        const string json = """[[["Hallo ","Hello "],["Welt","World"]],null,"en"]""";

        Assert.Equal("Hallo Welt", TranslatorService.ParseResponse(json));
    }

    [Fact]
    public void Reads_the_dict_chrome_ex_fallback_response()
    {
        const string json = """[["Hallo","en"]]""";

        Assert.Equal("Hallo", TranslatorService.ParseResponse(json));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[]]")]
    [InlineData("[null]")]
    public void Returns_empty_for_a_response_with_no_text(string json)
    {
        Assert.Equal(string.Empty, TranslatorService.ParseResponse(json));
    }

    [Theory]
    [InlineData("hello", true)]
    [InlineData("merhaba dünya", true)]
    [InlineData("gg1", true)]
    [InlineData("!!!", false)]
    [InlineData("123", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Only_treats_text_containing_letters_as_translatable(string? text, bool expected)
    {
        Assert.Equal(expected, TranslatorService.IsTranslatable(text));
    }
}
