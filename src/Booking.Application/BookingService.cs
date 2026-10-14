using Booking.Core;
using Booking.Core.Entities;
using Booking.Core.Exceptions;

namespace Booking.Application;

/// <summary>
/// Applies the booking rules.
/// </summary>
/// <remarks>
/// This is where the decisions live: a room must exist, a range must not overlap, and the check
/// and the write must be atomic. The repositories answer questions about stored data and the unit
/// of work decides where a transaction begins; neither knows any of those rules.
/// </remarks>
public sealed class BookingService(
    IRoomRepository rooms,
    IReservationRepository reservations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IBookingService
{
    public Task<IReadOnlyList<Room>> ListRoomsAsync(CancellationToken cancellationToken = default)
        => rooms.ListAsync(cancellationToken);

    public Task<IReadOnlyList<Room>> ListAvailableRoomsAsync(
        TimeRange window,
        CancellationToken cancellationToken = default)
    {
        EnsureWithinQueryWindow(window);

        return rooms.ListWithoutOverlapAsync(window, cancellationToken);
    }

    public Task<Room?> GetRoomAsync(int roomId, CancellationToken cancellationToken = default)
        => rooms.GetByIdAsync(roomId, cancellationToken);

    public Task<Reservation?> GetReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
        => reservations.GetByIdAsync(reservationId, cancellationToken);

    public async Task<IReadOnlyList<Reservation>> ListReservationsAsync(
        int roomId,
        TimeRange? window = null,
        CancellationToken cancellationToken = default)
    {
        if (window is { } range)
        {
            EnsureWithinQueryWindow(range);
        }

        if (await rooms.GetByIdAsync(roomId, cancellationToken) is null)
        {
            throw new RoomNotFoundException(roomId);
        }

        return await reservations.ListForRoomAsync(roomId, window, cancellationToken);
    }

    public async Task<Reservation> CreateReservationAsync(
        int roomId,
        TimeRange range,
        string title,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        EnsureWithinReservationLimit(range);

        if (await rooms.GetByIdAsync(roomId, cancellationToken) is null)
        {
            throw new RoomNotFoundException(roomId);
        }

        await ThrowIfOverlappingAsync(roomId, range, excludeReservationId: null, cancellationToken);

        var reservation = new Reservation
        {
            RoomId = roomId,
            Start = range.Start,
            End = range.End,
            Title = title,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        try
        {
            await reservations.AddAsync(reservation, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateReservationStartException)
        {
            // Another request committed a booking at the same start between our check and our
            // write. Losing that race is a conflict like any other, not a server error.
            throw new ReservationConflictException(
                await reservations.FindOverlappingAsync(
                    roomId, range, excludeReservationId: null, cancellationToken)
                ?? reservation);
        }

        await transaction.CommitAsync(cancellationToken);
        return reservation;
    }

    public async Task<Reservation> UpdateReservationAsync(
        int reservationId,
        TimeRange range,
        string title,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        EnsureWithinReservationLimit(range);

        var reservation = await reservations.GetByIdAsync(reservationId, cancellationToken)
                          ?? throw new ReservationNotFoundException(reservationId);

        // Excluding the reservation being edited is what makes rescheduling into a window that
        // only overlaps itself legal.
        await ThrowIfOverlappingAsync(
            reservation.RoomId, range, excludeReservationId: reservationId, cancellationToken);

        reservation.Start = range.Start;
        reservation.End = range.End;
        reservation.Title = title;

        try
        {
            // Already tracked by the read above, so this is an update, not an insert.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateReservationStartException)
        {
            throw new ReservationConflictException(
                await reservations.FindOverlappingAsync(
                    reservation.RoomId, range, excludeReservationId: reservationId, cancellationToken)
                ?? reservation);
        }

        await transaction.CommitAsync(cancellationToken);
        return reservation;
    }

    public async Task DeleteReservationAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, cancellationToken)
                          ?? throw new ReservationNotFoundException(reservationId);

        reservations.Remove(reservation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ThrowIfOverlappingAsync(
        int roomId,
        TimeRange range,
        int? excludeReservationId,
        CancellationToken cancellationToken)
    {
        var conflict = await reservations.FindOverlappingAsync(
            roomId, range, excludeReservationId, cancellationToken);

        if (conflict is not null)
        {
            throw new ReservationConflictException(conflict);
        }
    }

    /// <summary>
    /// A booking is a room for an hour or a day, not a venue for a fortnight.
    /// </summary>
    private static void EnsureWithinReservationLimit(TimeRange range)
    {
        if (range.Duration > BookingLimits.MaxReservationDuration)
        {
            throw new BookingRuleViolationException(
                "end",
                $"A reservation cannot span more than " +
                $"{BookingLimits.MaxReservationDuration.TotalHours:0} hours.");
        }
    }

    /// <summary>
    /// Neither availability lookups nor reservation listings may scan further than a week.
    /// </summary>
    private static void EnsureWithinQueryWindow(TimeRange window)
    {
        if (window.Duration > BookingLimits.MaxQueryWindow)
        {
            throw new BookingRuleViolationException(
                "end",
                $"A query window cannot span more than {BookingLimits.MaxQueryWindow.TotalDays:0} days.");
        }
    }
}