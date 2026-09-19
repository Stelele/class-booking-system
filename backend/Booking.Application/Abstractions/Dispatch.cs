using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Application.Abstractions;

public interface ICommand<TResponse> where TResponse : notnull { }

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse> where TResponse : notnull
{
    Task<TResponse> Handle(TCommand command, CancellationToken ct);
}

public interface IQuery<TResponse> where TResponse : notnull { }

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse> where TResponse : notnull
{
    Task<TResponse> Handle(TQuery query, CancellationToken ct);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}

public sealed class Sender(IServiceProvider sp) : ISender
{
    public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
        => InvokeHandler<TResponse>(command, typeof(ICommandHandler<,>), ct);

    public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken ct = default)
        => InvokeHandler<TResponse>(query, typeof(IQueryHandler<,>), ct);

    private Task<TResponse> InvokeHandler<TResponse>(object message, Type openGeneric, CancellationToken ct)
    {
        var concrete = openGeneric.MakeGenericType(message.GetType(), typeof(TResponse));
        var instance = sp.GetRequiredService(concrete);
        var method = concrete.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance)
                     ?? throw new InvalidOperationException($"No Handle on {concrete.Name}");
        return (Task<TResponse>)(method.Invoke(instance, [message, ct])
            ?? throw new InvalidOperationException("Handle returned null"));
    }
}

public static class HandlerRegistration
{
    public static IServiceCollection AddHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var asm in assemblies)
        foreach (var type in asm.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        foreach (var iface in type.GetInterfaces().Where(i =>
                     i.IsGenericType &&
                     i.GetGenericTypeDefinition() is var g &&
                     (g == typeof(ICommandHandler<,>) || g == typeof(IQueryHandler<,>))))
            services.AddScoped(iface, type);

        services.AddScoped<ISender, Sender>();
        return services;
    }
}
