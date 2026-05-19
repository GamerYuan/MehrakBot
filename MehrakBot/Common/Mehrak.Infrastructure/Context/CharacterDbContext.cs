using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

public class CharacterDbContext(DbContextOptions<CharacterDbContext> options) : DbContext(options)
{
    public DbSet<CharacterModel> Characters { get; set; }
    public DbSet<AliasModel> Aliases { get; set; }
    public DbSet<CharacterPortraitConfigModel> CharacterPortraitConfigs { get; set; }
    public DbSet<CharacterServerIdModel> CharacterServerIds { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterModel>()
            .HasMany(c => c.ServerIds)
            .WithOne(s => s.Character)
            .HasForeignKey(s => s.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
