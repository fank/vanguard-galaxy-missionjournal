using System;
using VGMissionJournal.Logging;
using VGMissionJournal.Tests.Support;
using Xunit;

namespace VGMissionJournal.Tests.Logging;

public class FakeClockTests
{
    [Fact]
    public void FakeClock_ImplementsIClock_AndReturnsSetValues()
    {
        IClock clock = new FakeClock { GameSeconds = 42.5, UtcNow = new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc) };

        Assert.Equal(42.5, clock.GameSeconds);
        Assert.Equal(new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc), clock.UtcNow);
    }

    [Fact]
    public void FakeClock_AdvanceGame_IncrementsGameSeconds()
    {
        var clock = new FakeClock { GameSeconds = 100 };

        clock.AdvanceGame(25.5);
        Assert.Equal(125.5, clock.GameSeconds);

        clock.AdvanceGame(-10);
        Assert.Equal(115.5, clock.GameSeconds);
    }

    [Fact]
    public void FakeClock_AdvanceReal_AddsTimeSpan()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock { UtcNow = start };

        clock.AdvanceReal(TimeSpan.FromMinutes(5));

        Assert.Equal(start.AddMinutes(5), clock.UtcNow);
    }

    [Fact]
    public void FakeClock_DefaultInstant_IsUtc2026Jan1()
    {
        var clock = new FakeClock();

        Assert.Equal(0.0, clock.GameSeconds);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }
}
