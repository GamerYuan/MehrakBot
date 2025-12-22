using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Auth;

public class DashboardAuthDbContext : DbContext
{
    public DashboardAuthDbContext(DbContextOptions<DashboardAuthDbContext> options) : base(options) { }

    public DbSet<DashboardUser> DashboardUsers => Set<DashboardUser>();
    public DbSet<DashboardSession> DashboardSessions => Set<DashboardSession>();
    public DbSet<DashboardGamePermission> DashboardGamePermissions => Set<DashboardGamePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DashboardUser>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Username).IsUnique();
            b.HasIndex(u => u.DiscordId).IsUnique();
            b.Property(u => u.Username).IsRequired().HasMaxLength(100);
            b.Property(u => u.PasswordHash).IsRequired();
            b.Property(u => u.DiscordId).IsRequired();
            b.Property(u => u.RequirePasswordReset).IsRequired();
        });

        modelBuilder.Entity<DashboardSession>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.SessionToken).IsUnique();
            b.Property(s => s.SessionToken).IsRequired().HasMaxLength(128);
            b.HasOne(s => s.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DashboardGamePermission>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.GameCode).IsRequired().HasMaxLength(64);
            b.HasIndex(p => new { p.UserId, p.GameCode }).IsUnique();
            b.HasOne(p => p.User)
                .WithMany(u => u.GamePermissions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
