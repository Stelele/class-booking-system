using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.BlockedDays;

public sealed class UnblockDayCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<UnblockDayCommand, bool>
{
    public async Task<bool> Handle(UnblockDayCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        var day = await db.BlockedDays.FirstOrDefaultAsync(b => b.Date == c.Date, ct);
        if (day is not null) { db.BlockedDays.Remove(day); await db.SaveChangesAsync(ct); }
        return true;
    }
}
