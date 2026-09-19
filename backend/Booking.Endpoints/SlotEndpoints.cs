using Booking.Application.Abstractions;
using Booking.Application.Slots;

namespace Booking.Endpoints;

public static class SlotEndpoints
{
    public static IEndpointRouteBuilder MapSlots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots", async (int year, int month, ISender sender) =>
            Results.Ok(await sender.Send(new GetMonthQuery(year, month))))
           .RequireAuthorization();
        return app;
    }
}
