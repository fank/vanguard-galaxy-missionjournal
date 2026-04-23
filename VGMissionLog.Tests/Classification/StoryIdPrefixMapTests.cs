using VGMissionLog.Classification;
using Xunit;

namespace VGMissionLog.Tests.Classification;

public class StoryIdPrefixMapTests
{
    [Fact]
    public void ExtractPrefix_VGAnimaConvention_ReturnsVganima()
    {
        Assert.Equal("vganima", StoryIdPrefixMap.ExtractPrefix("vganima_llm_abc123"));
    }

    [Fact]
    public void ExtractPrefix_ArbitraryPrefixBeforeLlm_IsReturned()
    {
        Assert.Equal("othermod", StoryIdPrefixMap.ExtractPrefix("othermod_llm_foo_bar"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractPrefix_NullOrEmpty_ReturnsNull(string? input)
    {
        Assert.Null(StoryIdPrefixMap.ExtractPrefix(input));
    }

    [Fact]
    public void ExtractPrefix_NoLlmInfix_ReturnsNull()
    {
        // Vanilla-style storyIds don't carry _llm_; must not be mistagged.
        Assert.Null(StoryIdPrefixMap.ExtractPrefix("tutorial_1"));
        Assert.Null(StoryIdPrefixMap.ExtractPrefix("sidequest_abc"));
        Assert.Null(StoryIdPrefixMap.ExtractPrefix("conquest_chapter_3"));
    }

    [Fact]
    public void ExtractPrefix_InfixAtStart_ReturnsNull()
    {
        // "_llm_orphan" has an empty prefix — defensive; treat as unrecognised.
        Assert.Null(StoryIdPrefixMap.ExtractPrefix("_llm_orphan"));
    }

    [Fact]
    public void ExtractPrefix_MultipleInfixOccurrences_StopsAtFirst()
    {
        Assert.Equal("a", StoryIdPrefixMap.ExtractPrefix("a_llm_b_llm_c"));
    }
}
