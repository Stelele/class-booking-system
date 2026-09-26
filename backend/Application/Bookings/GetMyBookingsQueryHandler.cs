using Application.Abstractions;
using Application.DTOs;
using Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Application.Bookings;

public sealed class GetMyBookingsQueryHandler(IAppDbContext db, ICurrentUser user)
    : IQueryHandler<GetMyBookingsQuery, List<BookingDto>>
{
    public async Task<List<BookingDto>> Handle(GetMyBookingsQuery q, CancellationToken ct)
    {
        // the name comes from the row, not the cookie claim, for the same
        // reason as GetMeQueryHandler — a rename must show up without re-login
        var me = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == user.UserId, ct);

        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.StudentId == user.UserId && b.Status == BookingStatus.Active)
            .Join(db.Slots, b => b.SlotId, s => s.Id, (b, s) => new { b, s })
            .OrderBy(x => x.s.Date)
            .ToListAsync(ct);

        return rows.Select(x => new BookingDto(
            x.b.Id, x.s.Date, LessonTime.StartUtc(x.s.Date), me?.Name ?? user.Name,
            CanCancel: true, CanReschedule: true,
            x.b.OriginalDate, x.s.MeetLink)).ToList();
    }
}
