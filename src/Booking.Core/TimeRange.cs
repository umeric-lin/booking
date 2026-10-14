namespace Booking.Core;

/// <summary>
/// A half-open interval <c>[Start, End)</c> of absolute time, always normalised to UTC.
/// </summary>
/// <remarks>
/// Half-open is what makes back-to-back bookings legal: <c>10:00-11:00</c> and <c>11:00-12:00</c>
/// do not overlap because the first interval excludes 11:00. Normalising to UTC in the
/// constructor means every stored value has the same offset, so SQLite's TEXT representation
/// of <see cref="DateTimeOffset"/> sorts and compares lexicographically in the same order it
/// sorts chronologically.
/// </remarks>
public readonly record struct TimeRange
{
    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        var utcStart = start.ToUniversalTime();
        var utcEnd = end.ToUniversalTime();

        if (utcEnd <= utcStart)
        {
            throw new ArgumentException("The end of a time range must be after its start.", nameof(end));
        }

        Start = utcStart;
        End = utcEnd;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    /// <summary>How long the range covers. Always positive, since the constructor rejects the rest.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>
    /// True when the two ranges share any instant. Touching ranges do not overlap.
    /// </summary>
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;
}