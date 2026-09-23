using Application.Abstractions;
using Application.Bookings;
using Application.DTOs;

namespace Endpoints;

public static class BookingEndpoints
{
    public sealed record CreateBookingRequest(DateOnly Date);
    public sealed record RescheduleRequest(DateOnly NewDate);

    public static IEndpointRouteBuilder MapBookings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").RequireAuthorization();

        group.MapGet("/mine", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMyBookingsQuery(), ct)));

        group.MapPost("", async (CreateBookingRequest req, ISender sender, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new CreateBookingCommand(req.Date), ct));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleRequest req, ISender sender, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new RescheduleBookingCommand(id, req.NewDate), ct));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try { return Results.Ok(await sender.Send(new CancelBookingCommand(id), ct)); }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
