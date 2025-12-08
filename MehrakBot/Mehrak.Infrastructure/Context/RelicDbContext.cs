using Mehrak.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

internal class RelicDbContext(DbContextOptions<RelicDbContext> options) : DbContext(options)
{
    public DbSet<HsrRelicModel> Relics { get; set; }
}
