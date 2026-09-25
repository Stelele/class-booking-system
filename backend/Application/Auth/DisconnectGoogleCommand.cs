using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed record DisconnectGoogleCommand : ICommand<GoogleDisconnectDto>;
