using Booking.Core.Entities;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure;

/// <summary>
/// Creates the schema and inserts the sample rooms on first run.
/// </summary>
public static class DatabaseInitializer
{
    private static readonly (string Name, int Capacity)[] SampleRooms =
    [
        ("Small Room", 4),
        ("Conference Room", 10),
        ("Auditorium", 50),
    ];

    /// <summary>
    /// Applies migrations and seeds the sample rooms if the rooms table is empty.
    /// </summary>
    /// <remarks>
    /// Seeding is idempotent and gated on emptiness rather than on "this is the first run", so
    /// deleting the database is never required to get the sample data back, and restarting the
    /// application never duplicates or overwrites a room an operator has since renamed.
    /// </remarks>
    public static async Task InitializeAsync(BookingDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (await dbContext.Rooms.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.Rooms.AddRange(SampleRooms.Select(r => new Room { Name = r.Name, Capacity = r.Capacity }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}