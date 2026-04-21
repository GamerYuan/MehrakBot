using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

public class DocumentationDbContext(DbContextOptions<DocumentationDbContext> options) : DbContext(options)
{
    public DbSet<DocumentationModel> Documentations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentationModel>(b =>
        {
            b.OwnsMany(d => d.Parameters, p =>
            {
                p.ToJson();
            });

            b.PrimitiveCollection(d => d.Examples);
        });
    }
}
