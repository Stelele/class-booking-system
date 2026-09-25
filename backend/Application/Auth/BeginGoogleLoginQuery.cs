using Application.Abstractions;

namespace Application.Auth;

public sealed record BeginGoogleLoginQuery(string Binding) : IQuery<string>;
