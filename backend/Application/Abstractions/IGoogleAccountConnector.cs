namespace Application.Abstractions;

public interface IGoogleAccountConnector
{
    Task ConnectAsync(string code, Guid userId, CancellationToken ct);
    Task<bool> DisconnectAsync(CancellationToken ct);
}
