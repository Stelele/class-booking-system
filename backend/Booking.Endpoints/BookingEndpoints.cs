using Booking.Application.Abstractions;
using Booking.Application.Bookings;
using Booking.Application.DTOs;

namespace Booking.Endpoints;

public static class BookingEndpoints
{
    public sealed record CreateBookingRequest(DateOnly Date);
    public sealed record RescheduleRequest(DateOnly NewDate);

    public static IEndpointRouteBuilder MapBookings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").RequireAuthorization();

        group.MapGet("/mine", async (ISender sender) =>
            Results.Ok(await sender.Send(new GetMyBookingsQuery())));

        group.MapPost("/", async (CreateBookingRequest req, ISender sender) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new CreateBookingCommand(req.Date)));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleRequest req, ISender sender) =>
        {
            try
            {
                return Results.Ok(await sender.Send(new RescheduleBookingCommand(id, req.NewDate)));
            }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            try { return Results.Ok(await sender.Send(new CancelBookingCommand(id))); }
            catch (BookingException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        return app;
    }
}
