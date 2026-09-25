using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record CompleteGoogleLoginCommand(string Code, string State, string Binding) : ICommand<UserDto>;
