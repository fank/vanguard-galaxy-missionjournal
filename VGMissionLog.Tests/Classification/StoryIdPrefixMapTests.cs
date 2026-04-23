using System;
using VGMissionLog.Classification;
using Xunit;

namespace VGMissionLog.Tests.Classification;

// Registration mutates process-global state; tests in this class each
// reset-to-default before running so they don't observe each other's
// registrations regardless of xUnit ordering.
public class StoryIdPrefixMapTests : IDisposable
{
    public StoryIdPrefixMapTests() => StoryIdPrefixMap.ResetToDefault();
    public void Dispose()          => StoryIdPrefixMap.ResetToDefault();

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
    public void ExtractPrefix_NoRecognisedInfix_ReturnsNull()
    {
        // Vanilla-style storyIds don't carry any registered infix.
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

    [Fact]
    public void Register_AddsNewInfix_Recognised()
    {
        Assert.Null(StoryIdPrefixMap.ExtractPrefix("foomod_missions_abc"));  // baseline

        StoryIdPrefixMap.Register("_missions_");

        Assert.Equal("foomod", StoryIdPrefixMap.ExtractPrefix("foomod_missions_abc"));
    }

    [Fact]
    public void Register_PreservesDefaultInfix()
    {
        StoryIdPrefixMap.Register("_custom_");

        // Both the built-in _llm_ and the newly-registered _custom_ should work.
        Assert.Equal("vganima",  StoryIdPrefixMap.ExtractPrefix("vganima_llm_abc"));
        Assert.Equal("foomod",   StoryIdPrefixMap.ExtractPrefix("foomod_custom_xyz"));
    }

    [Fact]
    public void Register_IsIdempotent()
    {
        StoryIdPrefixMap.Register("_custom_");
        StoryIdPrefixMap.Register("_custom_"); // no-op

        Assert.Equal("foo", StoryIdPrefixMap.ExtractPrefix("foo_custom_bar"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Register_NullOrEmpty_Throws(string? infix)
    {
        Assert.Throws<ArgumentException>(() => StoryIdPrefixMap.Register(infix!));
    }

    [Fact]
    public void ResetToDefault_RemovesRegisteredInfixes()
    {
        StoryIdPrefixMap.Register("_tempInfix_");
        Assert.Equal("foo", StoryIdPrefixMap.ExtractPrefix("foo_tempInfix_bar"));

        StoryIdPrefixMap.ResetToDefault();

        Assert.Null(StoryIdPrefixMap.ExtractPrefix("foo_tempInfix_bar"));
        Assert.Equal("vganima", StoryIdPrefixMap.ExtractPrefix("vganima_llm_abc"));
    }
}
