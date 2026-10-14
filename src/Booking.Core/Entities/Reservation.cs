namespace Booking.Core.Entities;

/// <summary>
/// A booking of one <see cref="Room"/> for a single <see cref="TimeRange"/>.
/// </summary>
public sealed class Reservation
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>Inclusive start of the booking.</summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>Exclusive end of the booking.</summary>
    public DateTimeOffset End { get; set; }

    public required string Title { get; set; }

    /// <summary>When the booking was made, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public TimeRange ToTimeRange() => new(Start, End);
}