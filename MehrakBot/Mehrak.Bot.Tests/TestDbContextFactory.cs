namespace Mehrak.Bot.Tests;

using System;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal sealed class TestDbContextFactory : IDisposable
{
    private readonly ServiceProvider m_ServiceProvider;
    public IServiceScopeFactory ScopeFactory { get; }
    public string DatabaseName { get; }

    public TestDbContextFactory(string? databaseName = null, Action<UserDbContext>? seed = null)
    {
        DatabaseName = databaseName ?? Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<UserDbContext>(options => options.UseInMemoryDatabase(DatabaseName));
        m_ServiceProvider = services.BuildServiceProvider();
        ScopeFactory = m_ServiceProvider.GetRequiredService<IServiceScopeFactory>();

        if (seed is not null)
        {
            using var scope = ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<UserDbContext>();
            seed(context);
            context.SaveChanges();
        }
    }

    public void Dispose()
    {
        m_ServiceProvider.Dispose();
    }
}
