using Application.Abstractions;
using Application.DTOs;

namespace Application.Auth;

public sealed class DisconnectGoogleCommandHandler(IGoogleAccountConnector connector)
    : ICommandHandler<DisconnectGoogleCommand, GoogleDisconnectDto>
{
    public async Task<GoogleDisconnectDto> Handle(
        DisconnectGoogleCommand command, CancellationToken ct) =>
        new(await connector.DisconnectAsync(ct));
}
