using Application.Abstractions;

namespace Application.Auth;

public sealed record CompleteGoogleOAuthCommand(string Code, string State) : ICommand<bool>;
