using VGMissionJournal.Logging;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class TimelineEntryTests
{
    [Fact]
    public void Entry_StoresStateAndGameSecondsAndOptionalRealUtc()
    {
        var e = new TimelineEntry(TimelineState.Accepted, 12450.0, "2026-04-24T14:12:30Z");
        Assert.Equal(TimelineState.Accepted, e.State);
        Assert.Equal(12450.0, e.GameSeconds);
        Assert.Equal("2026-04-24T14:12:30Z", e.RealUtc);
    }

    [Fact]
    public void Entry_RealUtcIsOptional()
    {
        var e = new TimelineEntry(TimelineState.Completed, 13420.0, RealUtc: null);
        Assert.Null(e.RealUtc);
    }

    [Theory]
    [InlineData(TimelineState.Accepted,  false)]
    [InlineData(TimelineState.Completed, true)]
    [InlineData(TimelineState.Failed,    true)]
    [InlineData(TimelineState.Abandoned, true)]
    public void Entry_IsTerminal_TrueForCompletedFailedAbandoned(TimelineState s, bool isTerminal)
    {
        var e = new TimelineEntry(s, 0.0, null);
        Assert.Equal(isTerminal, e.IsTerminal);
    }
}
