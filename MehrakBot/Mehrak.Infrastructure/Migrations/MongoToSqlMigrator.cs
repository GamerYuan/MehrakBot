using DnsClient.Internal;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
internal class MongoToSqlMigrator : IHostedService
{
    private readonly MongoDbService m_MongoService;
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<MongoToSqlMigrator> m_Logger;

    public MongoToSqlMigrator(
        MongoDbService mongoService,
        IServiceScopeFactory scopeFactory,
        ILogger<MongoToSqlMigrator> logger)
    {
        m_MongoService = mongoService;
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = m_ScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        if (await dbContext.Users.AnyAsync(cancellationToken: cancellationToken))
        {
            m_Logger.LogInformation("SQL database is not empty. Migration skipped.");
            return;
        }

        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var relicRepo = scope.ServiceProvider.GetRequiredService<IRelicRepository>();

        m_Logger.LogInformation("Migrating MongoDB to SQL database");

        await m_MongoService.Users.Find(_ => true).ForEachAsync(async mongoUser =>
        {
            var user = mongoUser.ToDto();
            await userRepo.CreateOrUpdateUserAsync(user);
        }, cancellationToken);

        await m_MongoService.HsrRelics.Find(_ => true).ForEachAsync(async mongoRelic =>
        {
            await relicRepo.AddSetName(mongoRelic.SetId, mongoRelic.SetName);
        }, cancellationToken);

        m_Logger.LogInformation("Migration Completed");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
