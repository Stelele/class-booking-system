namespace Booking.Domain.Users;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? PhoneE164 { get; set; }
    public UserRole Role { get; set; } = UserRole.Student;
    public required string TimeZoneId { get; set; } = "Europe/London";
}
