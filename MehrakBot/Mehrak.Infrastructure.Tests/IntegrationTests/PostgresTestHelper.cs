using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mehrak.Infrastructure.Tests.IntegrationTests;

internal sealed class PostgresTestHelper : IAsyncDisposable
{
    public static PostgresTestHelper Instance { get; } = new();

    private PostgreSqlContainer m_Db = null!;
    private string m_ConnString = string.Empty;

    public async Task InitAsync()
    {
        m_Db = new PostgreSqlBuilder()
            .WithImage("postgres:16.4")
            .WithUsername("test")
            .WithPassword("test")
            .WithDatabase("mehrak_test")
            .Build();
        await m_Db.StartAsync();

        m_ConnString = m_Db.GetConnectionString();

        // Apply migrations for all contexts against the container database
        using var userDbContext = CreateUserDbContext();
        await userDbContext.Database.MigrateAsync();

        using var characterDbContext = CreateCharacterDbContext();
        await characterDbContext.Database.MigrateAsync();

        using var relicDbContext = CreateRelicDbContext();
        await relicDbContext.Database.MigrateAsync();

        using var codeRedeemDbContext = CreateCodeRedeemDbContext();
        await codeRedeemDbContext.Database.MigrateAsync();
    }

    public UserDbContext CreateUserDbContext()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseNpgsql(m_ConnString)
            .Options;
        return new UserDbContext(options);
    }

    public CharacterDbContext CreateCharacterDbContext()
    {
        var options = new DbContextOptionsBuilder<CharacterDbContext>()
            .UseNpgsql(m_ConnString)
            .Options;
        return new CharacterDbContext(options);
    }

    public RelicDbContext CreateRelicDbContext()
    {
        var options = new DbContextOptionsBuilder<RelicDbContext>()
            .UseNpgsql(m_ConnString)
            .Options;
        return new RelicDbContext(options);
    }

    public CodeRedeemDbContext CreateCodeRedeemDbContext()
    {
        var options = new DbContextOptionsBuilder<CodeRedeemDbContext>()
            .UseNpgsql(m_ConnString)
            .Options;
        return new CodeRedeemDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await m_Db.StopAsync();
        await m_Db.DisposeAsync();
        m_Db = null!;
        m_ConnString = string.Empty;
    }
}
