using Booking.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the booking rules. The repositories and unit of work are supplied by the
    /// persistence layer, so the two can be replaced without touching this.
    /// </summary>
    public static IServiceCollection AddBookingApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IBookingService, BookingService>();

        return services;
    }
}