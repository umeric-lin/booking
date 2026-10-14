using Booking.Application;
using Booking.Core;
using Booking.Core.Entities;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure;

/// <summary>
/// EF Core-backed room reads. Queries only: no rules, no transaction handling.
/// </summary>
public sealed class SqliteRoomRepository(BookingDbContext dbContext) : IRoomRepository
{
    public async Task<IReadOnlyList<Room>> ListAsync(CancellationToken cancellationToken = default)
        => await dbContext.Rooms
            .AsNoTracking()
            .OrderBy(r => r.Capacity)
            .ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Room>> ListWithoutOverlapAsync(
        TimeRange window,
        CancellationToken cancellationToken = default)
    {
        // NOT EXISTS rather than a join, so a room is returned once no matter how many
        // reservations fall in the window. The inner lookup is served by
        // IX_Reservations_RoomId_Start.
        var sql =
            """
             SELECT r."Id", r."Name", r."Capacity"
             FROM "Rooms" AS r
             WHERE NOT EXISTS (
                 SELECT 1 FROM "Reservations" AS x
                 WHERE x."RoomId" = r."Id" AND x."Start" < {0} AND x."End" > {1}
             )
             ORDER BY r."Capacity", r."Name"
             """;

        return await dbContext.Rooms
            .FromSqlRaw(sql, window.End, window.Start)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Room?> GetByIdAsync(int roomId, CancellationToken cancellationToken = default)
        => await dbContext.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken);
}