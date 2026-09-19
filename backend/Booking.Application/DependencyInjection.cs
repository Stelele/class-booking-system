using Booking.Application.Abstractions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
        => services.AddHandlers(Assembly.GetExecutingAssembly());
}
