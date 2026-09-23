using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record VerifyCodeCommand(string Email, string Code) : ICommand<UserDto>;
