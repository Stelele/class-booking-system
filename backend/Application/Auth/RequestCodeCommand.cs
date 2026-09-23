using Application.Abstractions;

namespace Application.Auth;

public sealed record RequestCodeCommand(string Email) : ICommand<bool>;
