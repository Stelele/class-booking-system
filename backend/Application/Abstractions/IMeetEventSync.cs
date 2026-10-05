namespace Application.Abstractions;

public interface IMeetEventSync
{
    Task DeleteEventAsync(string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the event's guest list with <paramref name="attendeeEmails"/> and
    /// sends invitations. A full replace, not an append/remove pair: the caller
    /// always knows the complete set of students who should hold the day, which
    /// is what keeps a cancelled student from staying on the guest list.
    /// No-op when <paramref name="googleEventId"/> is null (fixed-link mode stores
    /// no real event). Never throws.
    /// </summary>
    Task UpdateAttendeesAsync(
        string? googleEventId, IReadOnlyList<string> attendeeEmails, CancellationToken ct = default);
}
