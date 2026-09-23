using Application.Abstractions;
using Application.Slots;

namespace Endpoints;

public static class SlotEndpoints
{
    public static IEndpointRouteBuilder MapSlots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/slots", async (int year, int month, ISender sender, CancellationToken ct) =>
        {
            if (year is < 1 or > 9999 || month is < 1 or > 12)
                return Results.BadRequest(new { error = "year must be 1-9999 and month 1-12" });
            return Results.Ok(await sender.Send(new GetMonthQuery(year, month), ct));
        }).RequireAuthorization();
        return app;
    }
}
