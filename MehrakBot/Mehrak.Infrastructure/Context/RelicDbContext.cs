using Mehrak.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

internal class RelicDbContext(DbContextOptions<RelicDbContext> options) : DbContext(options)
{
    public DbSet<HsrRelicModel> HsrRelics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HsrRelicModel>(entity =>
        {
            entity.ToTable("HsrRelics");
            entity.HasKey(e => e.SetId);
            entity.Property(e => e.SetName).IsRequired();
        });
    }
}
