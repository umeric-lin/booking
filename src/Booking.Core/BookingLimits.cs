namespace Booking.Core;

/// <summary>
/// The limits this booking system imposes, in one place so the rules cannot drift between the
/// endpoints that enforce them and the service that documents them.
/// </summary>
public static class BookingLimits
{
    /// <summary>
    /// A single booking may not span more than a day. A room is not a venue to move into.
    /// </summary>
    public static readonly TimeSpan MaxReservationDuration = TimeSpan.FromDays(1);

    /// <summary>
    /// A read window may not span more than a week. Applies to both availability queries
    /// (which rooms are free) and reservation listings (what is booked). Longer windows return
    /// little useful and would scan the whole table.
    /// </summary>
    public static readonly TimeSpan MaxQueryWindow = TimeSpan.FromDays(7);
}