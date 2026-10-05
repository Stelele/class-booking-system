namespace Application.DTOs;

/// <summary>
/// One student on a lesson the teacher has to teach. The booking id is what lets
/// the teacher cancel or move that student's booking — a combined lesson holds
/// two of them, and cancelling the day must not cancel both students.
/// </summary>
public sealed record TeacherLessonStudentDto(Guid BookingId, string Name);

/// <summary>
/// An upcoming lesson the teacher has to teach. The teacher has no bookings of
/// their own, so this is the only view that gives them the student's name and
/// the join link for a day — <see cref="SlotDayDto"/> carries the same Meet link
/// but nothing links a teacher to it in the UI.
/// </summary>
public sealed record TeacherLessonDto(
    DateOnly Date,
    DateTime StartUtc,
    DateTime EndUtc,
    /// <summary>Lesson wall-clock time in the lesson's own zone ("20:30"), which is the teacher's.</summary>
    string LocalTime,
    List<TeacherLessonStudentDto> Students,
    /// <summary>Null when the day has no meeting yet (fixed-link mode, or Google not connected).</summary>
    string? MeetLink,
    /// <summary>Both students share one room — the point of a combined lesson.</summary>
    bool IsCombined,
    /// <summary>
    /// An admin may cancel or move any booking, not only their own, so this is
    /// true for every row. Present so the page does not have to know that rule.
    /// </summary>
    bool CanCancel,
    bool CanReschedule)
{
    /// <summary>For the card's "student" / "2 students" label.</summary>
    public int StudentCount => Students.Count;

    /// <summary>
    /// Flat names, matching the shared-calendar vocabulary the page already
    /// renders a badge per student from.
    /// </summary>
    public List<string> StudentNames => Students.Select(s => s.Name).ToList();
}