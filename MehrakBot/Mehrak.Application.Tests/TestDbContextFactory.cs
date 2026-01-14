using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Application.Tests;

internal sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection m_Connection;

    public TestDbContextFactory()
    {
        m_Connection = new SqliteConnection($"Filename=:memory:");
        m_Connection.Open();
    }

    public TDb CreateDbContext<TDb>() where TDb : DbContext
    {
        var contextOptions = new DbContextOptionsBuilder<TDb>()
            .UseSqlite(m_Connection)
            .Options;

        return (TDb)Activator.CreateInstance(typeof(TDb), [contextOptions])!;
    }

    public void Dispose()
    {
        m_Connection.Dispose();
    }
}
