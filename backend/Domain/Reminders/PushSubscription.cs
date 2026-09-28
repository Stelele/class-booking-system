namespace Domain.Reminders;

/// <summary>A browser push endpoint registered by one of the user's devices.
/// A user may hold several (phone + tablet), so the endpoint — not the
/// user — is the unique key.</summary>
public sealed class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string Endpoint { get; set; }
    public required string P256Dh { get; set; }
    public required string Auth { get; set; }

    /// <summary>
    /// The registering device is iOS/iPadOS. Web Push returns "accepted by the
    /// push service", never "displayed on the device" — so on iOS we cannot
    /// prove a notification was actually shown. Because a silently dropped
    /// reminder means a missed lesson, and email is free, iOS subscriptions
    /// also receive the email copy. See PushOrEmailNotifier.
    /// </summary>
    public bool IsIos { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
