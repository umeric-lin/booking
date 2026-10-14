namespace Booking.Core.Exceptions;

/// <summary>
/// Thrown when a reservation is addressed by an id that does not exist.
/// </summary>
public sealed class ReservationNotFoundException(int reservationId)
    : Exception($"Reservation {reservationId} does not exist.")
{
    public int ReservationId { get; } = reservationId;
}