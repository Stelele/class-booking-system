using Booking.Application.Abstractions;
using Booking.Application.DTOs;

namespace Booking.Application.Auth;

public sealed record VerifyCodeCommand(string Email, string Code) : ICommand<UserDto>;
