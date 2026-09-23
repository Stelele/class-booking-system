using Application.Abstractions;

namespace Application.Auth;

public sealed record BeginGoogleOAuthQuery : IQuery<string>;
