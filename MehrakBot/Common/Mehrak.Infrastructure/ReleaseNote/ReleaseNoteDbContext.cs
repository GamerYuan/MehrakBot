using Mehrak.Infrastructure.ReleaseNote.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.ReleaseNote;

public class ReleaseNoteDbContext(DbContextOptions<ReleaseNoteDbContext> options) : DbContext(options)
{
    public DbSet<ReleaseVersionModel> ReleaseVersions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReleaseVersionModel>(b =>
        {
            b.OwnsMany(r => r.Sections, s =>
            {
                s.ToJson();
                s.OwnsMany(n => n.Notes);
            });
        });
    }
}
