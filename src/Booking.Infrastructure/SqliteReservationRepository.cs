using Booking.Application;
using Booking.Core;
using Booking.Core.Entities;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure;

/// <summary>
/// EF Core-backed reservation reads and staged writes. Queries only: no rules, no transaction
/// handling, no decisions about what a result means.
/// </summary>
public sealed class SqliteReservationRepository(BookingDbContext dbContext) : IReservationRepository
{
    /// <summary>
    /// Half-open interval overlap: an existing reservation overlaps when it starts before the
    /// range's end and ends after its start. Reservations that merely touch (10:00-11:00 and
    /// 11:00-12:00) fail both comparisons, which is why they do not overlap.
    /// </summary>
    /// <remarks>
    /// Written as SQL because SQLite's provider cannot translate comparisons between
    /// <see cref="DateTimeOffset"/> columns. <c>Start &lt; @end AND End &gt; @start</c> is served
    /// by <c>IX_Reservations_RoomId_Start</c>: the index narrows to one room, then ranges over
    /// Start, so the lookup touches only that room's bookings. The definition this implements is
    /// <see cref="TimeRange.Overlaps"/>; this query must keep agreeing with it.
    /// </remarks>
    private const string OverlapPredicate =
        """SELECT "Id", "RoomId", "Start", "End", "Title", "CreatedAt" FROM "Reservations" WHERE "RoomId" = {0} AND "Start" < {1} AND "End" > {2}""";

    private const string RoomPredicate =
        """SELECT "Id", "RoomId", "Start", "End", "Title", "CreatedAt" FROM "Reservations" WHERE "RoomId" = {0}""";

    public async Task<IReadOnlyList<Reservation>> ListForRoomAsync(
        int roomId,
        TimeRange? window = null,
        CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = window is { } range
            ? (OverlapPredicate, new object[] { roomId, range.End, range.Start })
            : (RoomPredicate, new object[] { roomId });

        var reservations = await dbContext.Reservations
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // SQLite's provider refuses to ORDER BY a DateTimeOffset column, so the ordering is
        // applied here. The result is already bounded to a single room, so it stays small. If
        // that assumption ever stops holding, the fix is a value converter storing UTC ticks
        // into a sortable integer column rather than moving the sort to the client.
        return [.. reservations.OrderBy(r => r.Start)];
    }

    /// <remarks>
    /// Tracked, so the caller can modify the result and save it without a second round trip.
    /// </remarks>
    public async Task<Reservation?> GetByIdAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
        => await dbContext.Reservations
            .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken);

    public async Task<Reservation?> FindOverlappingAsync(
        int roomId,
        TimeRange range,
        int? excludeReservationId = null,
        CancellationToken cancellationToken = default)
    {
        var sql = excludeReservationId is null
            ? OverlapPredicate + " LIMIT 1"
            : OverlapPredicate + """ AND "Id" <> {3} LIMIT 1""";

        var parameters = excludeReservationId is { } exclude
            ? new object[] { roomId, range.End, range.Start, exclude }
            : new object[] { roomId, range.End, range.Start };

        return await dbContext.Reservations
            .FromSqlRaw(sql, parameters)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
        => await dbContext.Reservations.AddAsync(reservation, cancellationToken);

    public void Remove(Reservation reservation) => dbContext.Reservations.Remove(reservation);
}