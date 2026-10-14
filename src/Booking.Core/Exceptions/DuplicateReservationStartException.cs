namespace Booking.Application;

/// <summary>
/// The domain fact that a room cannot have two reservations starting at the same instant.
/// </summary>
/// <remarks>
/// Raised by the persistence layer when the database's unique index refuses an insert, so that
/// the SQLite error code — which only the persistence layer understands — is translated into a
/// statement about the domain at the boundary. Deciding what this <em>means</em> for a booking is
/// the service's job; see <c>BookingService.CreateAsync</c>.
/// </remarks>
public sealed class DuplicateReservationStartException(int roomId, DateTimeOffset start)
    : Exception($"Room {roomId} already has a reservation starting at {start:O}.")
{
    public int RoomId { get; } = roomId;

    public DateTimeOffset Start { get; } = start;
}