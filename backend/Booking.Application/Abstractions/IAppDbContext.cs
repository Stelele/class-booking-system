using Booking.Domain.Auth;
using Booking.Domain.Backups;
using Booking.Domain.Slots;
using Booking.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using BookingEntity = Booking.Domain.Slots.Booking;

namespace Booking.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Slot> Slots { get; }
    DbSet<BookingEntity> Bookings { get; }
    DbSet<BlockedDay> BlockedDays { get; }
    DbSet<AuthCode> AuthCodes { get; }
    DbSet<BackupLog> BackupLogs { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
