using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

internal class UserContext(DbContextOptions<UserContext> options) : DbContext(options)
{
    public DbSet<UserModel> Users { get; set; }
    public DbSet<UserProfileModel> UserProfiles { get; set; }
    public DbSet<ProfileGameUid> GameUids { get; set; }
    public DbSet<ProfileRegion> Regions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. User -> UserProfile (Cascade Delete)
        modelBuilder.Entity<UserModel>()
            .HasMany(u => u.Profiles)
            .WithOne(p => p.User)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting User deletes Profiles

        // 2. UserProfile -> ProfileGameUid (Cascade Delete)
        modelBuilder.Entity<UserProfileModel>()
            .HasMany(p => p.GameUids)
            .WithOne(g => g.UserProfile)
            .HasForeignKey(g => g.ProfileId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting Profile deletes GameUids

        // 3. UserProfile -> ProfileRegion (Cascade Delete)
        modelBuilder.Entity<UserProfileModel>()
            .HasMany(p => p.LastUsedRegions)
            .WithOne(r => r.UserProfile)
            .HasForeignKey(r => r.ProfileId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting Profile deletes Regions
    }
}
