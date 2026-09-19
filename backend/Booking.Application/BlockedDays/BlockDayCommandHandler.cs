using Booking.Application.Abstractions;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;

namespace Booking.Application.BlockedDays;

public sealed class BlockDayCommandHandler(IAppDbContext db, ICurrentUser user)
    : ICommandHandler<BlockDayCommand, bool>
{
    public async Task<bool> Handle(BlockDayCommand c, CancellationToken ct)
    {
        if (!user.IsAdmin) throw new UnauthorizedAccessException("Admin only.");
        if (await db.BlockedDays.AnyAsync(b => b.Date == c.Date, ct)) return true;
        db.BlockedDays.Add(new BlockedDay { Date = c.Date, Reason = c.Reason });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
