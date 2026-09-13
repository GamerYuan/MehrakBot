using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mehrak.Infrastructure.Character;

public sealed class CharacterDbContextFactory : IDesignTimeDbContextFactory<CharacterDbContext>
{
    public CharacterDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__mehrakdb")
            ?? "Host=localhost;Database=mehrakdb;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CharacterDbContext(options);
    }
}
