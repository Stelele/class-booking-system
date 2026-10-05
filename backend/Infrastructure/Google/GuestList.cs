namespace Infrastructure.Google;

/// <summary>
/// Builds the <c>attendees</c> payload for a Calendar event. Duplicates and
/// blanks are dropped so a double-booked day cannot make Google send the same
/// invitation twice.
///
/// The insert and patch paths need <em>opposite</em> behaviour when there is
/// nobody to invite, which is why this is not one shared helper:
/// <list type="bullet">
/// <item>On insert, omit the key entirely — an explicit empty array is a
/// different statement ("invite nobody"), and with sendUpdates=all it produces
/// exactly the silent no-invite behaviour this replaces.</item>
/// <item>On patch, send <c>attendees: []</c> — the patch is a full replace, and
/// a body with no attendees key leaves the existing guests untouched. Omitting
/// it there would keep a cancelled student invited to a lesson they left.</item>
/// </list>
/// </summary>
internal static class GuestList
{
    /// <summary>Guests for an insert: null (key omitted) when the list is empty.</summary>
    public static object[]? ForInsert(IReadOnlyList<string> emails) =>
        Clean(emails) is { Length: > 0 } guests ? guests : null;

    /// <summary>
    /// Guests for a patch. Always an array, empty included, because a patch
    /// without the key is a no-op rather than a clear.
    /// </summary>
    public static object[] ForReplace(IReadOnlyList<string> emails) => Clean(emails);

    private static object[] Clean(IReadOnlyList<string> emails) =>
    [
        .. emails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(email => (object)new { email }),
    ];
}