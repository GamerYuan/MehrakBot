using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

public class CharacterDbContext(DbContextOptions<CharacterDbContext> options) : DbContext(options)
{
    public DbSet<CharacterModel> Characters { get; set; }
    public DbSet<AliasModel> Aliases { get; set; }
}
