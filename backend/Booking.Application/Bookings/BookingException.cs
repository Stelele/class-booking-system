namespace Booking.Application.Bookings;

public sealed class BookingException(string message) : Exception(message);
