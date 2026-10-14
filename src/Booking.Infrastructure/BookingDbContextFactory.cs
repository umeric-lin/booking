using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Booking.Infrastructure;

/// <summary>
/// Used by <c>dotnet ef</c> to build a context at design time. It deliberately does not read
/// application configuration, so migrations do not depend on how the app happens to be hosted.
/// </summary>
public sealed class BookingDbContextFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseSqlite("Data Source=booking-design-time.db")
            .Options;

        return new BookingDbContext(options);
    }
}