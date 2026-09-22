using Booking.Application.Abstractions;
using Booking.Domain.Auth;
using Booking.Domain.Backups;
using Booking.Domain.Users;
using Booking.Domain.Slots;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Booking.Domain.Slots.Booking;

namespace Booking.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<BlockedDay> BlockedDays => Set<BlockedDay>();
    public DbSet<AuthCode> AuthCodes => Set<AuthCode>();
    public DbSet<GoogleToken> GoogleTokens => Set<GoogleToken>();
    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Name).HasMaxLength(100);
        });
        mb.Entity<Slot>(e =>
        {
            e.HasIndex(s => s.Date).IsUnique();
            e.Property(s => s.MeetLink).HasMaxLength(300);
        });
        mb.Entity<BookingEntity>(e =>
        {
            // Active bookings unique per (slot, student); cancelled rows may repeat.
            e.HasIndex(b => new { b.SlotId, b.StudentId, b.Status }).IsUnique().HasFilter("Status = 1");
            e.HasOne(b => b.Slot).WithMany(s => s.Bookings).HasForeignKey(b => b.SlotId);
        });
        mb.Entity<BlockedDay>(e => e.HasKey(b => b.Date));
        mb.Entity<AuthCode>(e => e.HasIndex(a => a.UserId));
        mb.Entity<GoogleToken>(e =>
        {
            e.HasIndex(t => t.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
