using Application.Abstractions;

namespace Infrastructure.Meet;

public sealed class FixedLinkEventSync : IMeetEventSync
{
    public Task DeleteEventAsync(string googleEventId, CancellationToken ct) => Task.CompletedTask;
}
