using Application.Abstractions;
using Application.DTOs;

namespace Application.Users;

public sealed record UpdateUserCommand(Guid Id, string Name, string Email, string? Phone)
    : ICommand<AdminUserDto>;
