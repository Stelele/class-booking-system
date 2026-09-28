namespace Application.Abstractions;

/// <summary>
/// How time-sensitive a notification is. Maps to the push protocol's urgency
/// and to email priority; deliberately provider-agnostic so the Application
/// layer never references a messaging vendor's concepts.
/// </summary>
public enum NotifyUrgency
{
    Normal,
    High,
}

/// <summary>
/// Delivers a notification to a user. Implementations must never throw:
/// notification failures are recorded in ReminderLog and must never be able
/// to break a booking.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Pushes to the user's devices, falling back to email when nothing
    /// delivers. Returns the channel actually used.
    /// </summary>
    Task<NotifyResult> SendAsync(
        Guid userId,
        string title,
        string body,
        NotifyUrgency urgency = NotifyUrgency.Normal,
        CancellationToken ct = default);
}

/// <param name="Channel">"push", "email" or "log".</param>
/// <param name="ProviderRef">
/// The provider's message id when one exists; null for web push, which has no
/// per-message identifier.
/// </param>
public sealed record NotifyResult(string Channel, string? ProviderRef = null);
