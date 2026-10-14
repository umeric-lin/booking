using Booking.Core;
using Booking.Core.Entities;

namespace Booking.Application;

/// <summary>
/// Read and write access to reservations. No rules, no decisions, no transaction boundaries.
/// </summary>
public interface IReservationRepository
{
    /// <summary>
    /// A room's reservations, earliest first, optionally narrowed to those overlapping
    /// <paramref name="window"/>.
    /// </summary>
    Task<IReadOnlyList<Reservation>> ListForRoomAsync(
        int roomId,
        TimeRange? window = null,
        CancellationToken cancellationToken = default);

    Task<Reservation?> GetByIdAsync(
        int reservationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The first reservation in the room overlapping <paramref name="range"/>, or null if the room
    /// is free for it. Ranges are half-open, so a reservation ending exactly at
    /// <paramref name="range"/>'s start does not overlap; see <see cref="TimeRange.Overlaps"/>
    /// for the definition this query implements.
    /// </summary>
    /// <param name="excludeReservationId">
    /// A reservation to ignore, used when rescheduling one so it cannot conflict with itself.
    /// </param>
    Task<Reservation?> FindOverlappingAsync(
        int roomId,
        TimeRange range,
        int? excludeReservationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new reservation. Nothing is written until the unit of work is saved.</summary>
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);

    /// <summary>Stages the removal of an existing reservation.</summary>
    void Remove(Reservation reservation);
}