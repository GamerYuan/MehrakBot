using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Auth;

public class DashboardAuthDbContext : DbContext
{
    public DashboardAuthDbContext(DbContextOptions<DashboardAuthDbContext> options) : base(options) { }

    public DbSet<DashboardPermission> DashboardPermissions => Set<DashboardPermission>();
    public DbSet<DashboardSession> DashboardSessions => Set<DashboardSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DashboardPermission>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.DiscordId).IsRequired();
            b.Property(p => p.Permission).IsRequired().HasMaxLength(128);
            b.HasIndex(p => p.DiscordId);
            b.HasIndex(p => new { p.DiscordId, p.Permission }).IsUnique();
        });

        modelBuilder.Entity<DashboardSession>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Token).IsRequired().HasMaxLength(32);
            b.Property(s => s.LoginIp).HasMaxLength(45);
            b.Property(s => s.UserAgent).HasMaxLength(512);
            b.Property(s => s.Location).HasMaxLength(256);
            b.HasIndex(s => s.Token).IsUnique();
            b.HasIndex(s => s.DiscordId);
            b.HasIndex(s => s.ExpiresAt);
        });
    }
}
