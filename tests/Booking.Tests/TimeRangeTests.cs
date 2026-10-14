using Booking.Core;
using Xunit;

namespace Booking.Tests;

public class TimeRangeTests
{
    private static readonly DateTimeOffset Ten = new(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Overlaps_WhenRangesShareTime()
    {
        var existing = new TimeRange(Ten, Ten.AddHours(1));
        var requested = new TimeRange(Ten.AddMinutes(30), Ten.AddHours(1.5));

        Assert.True(existing.Overlaps(requested));
        Assert.True(requested.Overlaps(existing));
    }

    [Fact]
    public void DoesNotOverlap_WhenSecondRangeStartsExactlyAtFirstEnd()
    {
        var existing = new TimeRange(Ten, Ten.AddHours(1));
        var backToBack = new TimeRange(Ten.AddHours(1), Ten.AddHours(2));

        Assert.False(existing.Overlaps(backToBack));
        Assert.False(backToBack.Overlaps(existing));
    }

    [Fact]
    public void DoesNotOverlap_WhenFirstRangeStartsExactlyAtSecondEnd()
    {
        var existing = new TimeRange(Ten, Ten.AddHours(1));
        var before = new TimeRange(Ten.AddHours(-1), Ten);

        Assert.False(existing.Overlaps(before));
    }

    [Fact]
    public void Overlaps_WhenOneRangeContainsTheOther()
    {
        var outer = new TimeRange(Ten, Ten.AddHours(10));
        var inner = new TimeRange(Ten.AddHours(2), Ten.AddHours(3));

        Assert.True(outer.Overlaps(inner));
        Assert.True(inner.Overlaps(outer));
    }

    [Fact]
    public void DoesNotOverlap_WhenRangesAreDisjoint()
    {
        Assert.False(new TimeRange(Ten, Ten.AddHours(1))
            .Overlaps(new TimeRange(Ten.AddDays(1), Ten.AddDays(1).AddHours(1))));
    }

    [Fact]
    public void NormalisesToUtc()
    {
        var berlin = new DateTimeOffset(2026, 1, 5, 11, 0, 0, TimeSpan.FromHours(1));

        var range = new TimeRange(berlin, berlin.AddHours(1));

        Assert.Equal(TimeSpan.Zero, range.Start.Offset);
        Assert.Equal(TimeSpan.Zero, range.End.Offset);
        Assert.Equal(Ten, range.Start);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Rejects_RangeThatDoesNotMoveForward(int minutes)
    {
        Assert.Throws<ArgumentException>(() => new TimeRange(Ten, Ten.AddMinutes(minutes)));
    }

    [Fact]
    public void Contains_IsHalfOpen()
    {
        var range = new TimeRange(Ten, Ten.AddHours(1));

        Assert.True(range.Contains(Ten));
        Assert.True(range.Contains(Ten.AddHours(1).AddSeconds(-1)));
        Assert.False(range.Contains(Ten.AddHours(1)));
    }
}