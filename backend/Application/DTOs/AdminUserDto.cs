namespace Application.DTOs;

/// Admin-facing view of a user. Carries the phone so the admin "People" card can
/// edit name, email and number in one place; <see cref="UserDto"/> is the
/// trimmed-down shape the app itself consumes.
public sealed record AdminUserDto(Guid Id, string Name, string Email, string? Phone, string Role);
