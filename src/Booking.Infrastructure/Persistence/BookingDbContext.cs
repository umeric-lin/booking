using Booking.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Booking.Infrastructure.Persistence;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
    }
}