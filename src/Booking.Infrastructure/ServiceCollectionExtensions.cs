using Booking.Application;
using Booking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite-backed data access layer: repositories that only read and stage
    /// changes, plus the unit of work that owns transaction boundaries. No booking rules here.
    /// </summary>
    public static IServiceCollection AddBookingPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<BookingDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IRoomRepository, SqliteRoomRepository>();
        services.AddScoped<IReservationRepository, SqliteReservationRepository>();
        services.AddScoped<IUnitOfWork, SqliteUnitOfWork>();

        return services;
    }
}