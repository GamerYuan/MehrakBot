using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Extensions;
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
        var userDbContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        var relicDbContext = scope.ServiceProvider.GetRequiredService<RelicDbContext>();

        if (await userDbContext.Users.AnyAsync(cancellationToken: cancellationToken) ||
            await relicDbContext.HsrRelics.AnyAsync(cancellationToken: cancellationToken))
        {
            m_Logger.LogInformation("SQL database is not empty. Migration skipped.");
            return;
        }

        m_Logger.LogInformation("Migrating MongoDB to SQL database");

        await using var userTransaction = await userDbContext.Database.BeginTransactionAsync(cancellationToken);
        await using var relicTransaction = await relicDbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var mongoUsers = await m_MongoService.Users.Find(_ => true).ToListAsync(cancellationToken);
            var failedUsers = 0;

            foreach (var mongoUser in mongoUsers)
            {
                try
                {
                    var user = mongoUser.ToDto();
                    userDbContext.Users.Add(user.ToUserModel());
                }
                catch (Exception e)
                {
                    m_Logger.LogError(e, "Failed to migrate user {UserId}", mongoUser.Id);
                    failedUsers++;
                }
            }

            await userDbContext.SaveChangesAsync(cancellationToken);
            await userTransaction.CommitAsync(cancellationToken);

            var mongoRelics = await m_MongoService.HsrRelics.Find(_ => true).ToListAsync(cancellationToken);
            var failedRelics = 0;

            foreach (var mongoRelic in mongoRelics)
            {
                try
                {
                    relicDbContext.HsrRelics.Add(new HsrRelicModel { SetId = mongoRelic.SetId, SetName = mongoRelic.SetName });
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "Failed to migrate relic {SetId}", mongoRelic.SetId);
                    failedRelics++;
                }
            }

            await relicDbContext.SaveChangesAsync(cancellationToken);
            await relicTransaction.CommitAsync(cancellationToken);

            m_Logger.LogInformation("Migration completed. Users: {TotalUsers} (Failed: {FailedUsers}), Relics: {TotalRelics} (Failed: {FailedRelics})",
                mongoUsers.Count, failedUsers, mongoRelics.Count, failedRelics);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Migration failed and was rolled back");
            await userTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
