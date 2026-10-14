namespace Booking.Core.Exceptions;

/// <summary>
/// Thrown when a booking request collides with an existing reservation in the same room.
/// </summary>
/// <remarks>
/// The API translates this into <c>409 Conflict</c>. The conflicting reservation is carried
/// on the exception so the response can tell the caller <em>what</em> they collided with
/// rather than just that they did.
/// </remarks>
public sealed class ReservationConflictException : Exception
{
    public ReservationConflictException(Entities.Reservation conflictingReservation)
        : base($"Room {conflictingReservation.RoomId} is already reserved from " +
               $"{conflictingReservation.Start:O} to {conflictingReservation.End:O}.")
    {
        ConflictingReservation = conflictingReservation;
    }

    public Entities.Reservation ConflictingReservation { get; }
}