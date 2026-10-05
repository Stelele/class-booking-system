using Application.Abstractions;

namespace Infrastructure.Meet;

public sealed class FixedLinkEventSync : IMeetEventSync
{
    public Task DeleteEventAsync(string googleEventId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAttendeesAsync(
        string? googleEventId, IReadOnlyList<string> attendeeEmails, CancellationToken ct = default)
        => Task.CompletedTask;
}
