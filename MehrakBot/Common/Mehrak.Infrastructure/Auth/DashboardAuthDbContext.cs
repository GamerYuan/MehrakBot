using Mehrak.Infrastructure.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Auth;

public class DashboardAuthDbContext : DbContext
{
    public DashboardAuthDbContext(DbContextOptions<DashboardAuthDbContext> options) : base(options) { }

    public DbSet<DashboardPermission> DashboardPermissions => Set<DashboardPermission>();

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
    }
}
