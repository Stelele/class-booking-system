using Booking.Application.Abstractions;
using Booking.Application.Notifications;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddHandlers(Assembly.GetExecutingAssembly());
        services.AddScoped<IBookingNotifier, BookingNotifier>();
        return services;
    }
}
