namespace Domain.Reminders;

public sealed class ReminderLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning user. Nullable because rows written before push notifications
    /// existed were keyed by phone number; the migration backfills this by
    /// matching <see cref="To"/> against a user's phone where possible.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Human-readable destination for debugging (an email address).</summary>
    public required string To { get; set; }

    public required DateOnly Date { get; set; }
    public required string Template { get; set; }
    public required string Result { get; set; }

    /// <summary>Channel used: "push", "email" or "log".</summary>
    public string Channel { get; set; } = "log";

    /// <summary>
    /// Provider's message id when one exists. Null for web push — the browser
    /// push protocol has no per-message identifier.
    /// </summary>
    public string? ProviderRef { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
