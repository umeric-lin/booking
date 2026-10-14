using Booking.Core;
using Booking.Core.Entities;

namespace Booking.Application;

/// <summary>
/// The booking rules, applied over stored data.
/// </summary>
public interface IBookingService
{
    Task<IReadOnlyList<Room>> ListRoomsAsync(CancellationToken cancellationToken = default);

    /// <summary>Rooms free for the whole of <paramref name="window"/>.</summary>
    /// <exception cref="Exceptions.BookingRuleViolationException">
    /// The window spans longer than <see cref="BookingLimits.MaxQueryWindow"/>.
    /// </exception>
    Task<IReadOnlyList<Room>> ListAvailableRoomsAsync(
        TimeRange window,
        CancellationToken cancellationToken = default);

    Task<Room?> GetRoomAsync(int roomId, CancellationToken cancellationToken = default);

    /// <summary>One reservation, or null if no reservation has that id.</summary>
    Task<Reservation?> GetReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A room's reservations, optionally narrowed to those intersecting <paramref name="window"/>.
    /// </summary>
    /// <exception cref="Exceptions.RoomNotFoundException">No room has that id.</exception>
    /// <exception cref="Exceptions.BookingRuleViolationException">
    /// The window spans longer than <see cref="BookingLimits.MaxQueryWindow"/>.
    /// </exception>
    Task<IReadOnlyList<Reservation>> ListReservationsAsync(
        int roomId,
        TimeRange? window = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Books <paramref name="range"/> in <paramref name="roomId"/>, rejecting an overlap with an
    /// existing reservation.
    /// </summary>
    /// <exception cref="Exceptions.RoomNotFoundException">No room has that id.</exception>
    /// <exception cref="Exceptions.BookingRuleViolationException">
    /// The range spans longer than <see cref="BookingLimits.MaxReservationDuration"/>.
    /// </exception>
    /// <exception cref="Exceptions.ReservationConflictException">The range overlaps an existing reservation.</exception>
    Task<Reservation> CreateReservationAsync(
        int roomId,
        TimeRange range,
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reschedules and/or retitles a reservation. The new range is checked against every other
    /// reservation in the room; overlapping only itself is allowed.
    /// </summary>
    /// <exception cref="Exceptions.ReservationNotFoundException">No reservation has that id.</exception>
    /// <exception cref="Exceptions.BookingRuleViolationException">
    /// The range spans longer than <see cref="BookingLimits.MaxReservationDuration"/>.
    /// </exception>
    /// <exception cref="Exceptions.ReservationConflictException">The range overlaps a different reservation.</exception>
    Task<Reservation> UpdateReservationAsync(
        int reservationId,
        TimeRange range,
        string title,
        CancellationToken cancellationToken = default);

    /// <exception cref="Exceptions.ReservationNotFoundException">No reservation has that id.</exception>
    Task DeleteReservationAsync(int reservationId, CancellationToken cancellationToken = default);
}