using Domain.Auth;
using Domain.Backups;
using Domain.Reminders;
using Domain.Slots;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using BookingEntity = Domain.Slots.Booking;

namespace Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Slot> Slots { get; }
    DbSet<BookingEntity> Bookings { get; }
    DbSet<BlockedDay> BlockedDays { get; }
    DbSet<AuthCode> AuthCodes { get; }
    DbSet<BackupLog> BackupLogs { get; }
    DbSet<GoogleToken> GoogleTokens { get; }
    DbSet<ReminderLog> ReminderLogs { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
