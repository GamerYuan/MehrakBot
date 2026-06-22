using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Tests.TestUtils;

internal sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection m_Connection;

    public TestDbContextFactory()
    {
        m_Connection = new SqliteConnection("Filename=:memory:");
        m_Connection.Open();
    }

    public TDb CreateDbContext<TDb>() where TDb : DbContext
    {
        var contextOptions = CreateContextOptions<TDb>();
        var dbContext = (TDb)Activator.CreateInstance(typeof(TDb), [contextOptions])!;
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    public DbContextOptions<TDb> CreateContextOptions<TDb>() where TDb : DbContext
    {
        return new DbContextOptionsBuilder<TDb>()
            .UseSqlite(m_Connection)
            .Options;
    }

    public void Dispose()
    {
        m_Connection.Dispose();
    }
}
