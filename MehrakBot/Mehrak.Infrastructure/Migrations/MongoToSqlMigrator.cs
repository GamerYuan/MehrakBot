using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
internal class MongoToSqlMigrator : IHostedService
{
    private readonly MongoDbService m_MongoService;
    private readonly UserDbContext m_DbContext;
    private readonly IUserRepository m_UserRepo;
    private readonly IRelicRepository m_RelicRepo;
    private readonly ILogger<MongoToSqlMigrator> m_Logger;

    public MongoToSqlMigrator(
        MongoDbService mongoService,
        UserDbContext dbContext,
        IUserRepository userRepo,
        IRelicRepository relicRepo,
        ILogger<MongoToSqlMigrator> logger)
    {
        m_MongoService = mongoService;
        m_DbContext = dbContext;
        m_UserRepo = userRepo;
        m_RelicRepo = relicRepo;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await m_DbContext.Users.AnyAsync(cancellationToken: cancellationToken))
        {
            m_Logger.LogInformation("SQL database is not empty. Migration skipped.");
            return;
        }

        m_Logger.LogInformation("Migrating MongoDB to SQL database");

        using var transaction = await m_DbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var mongoUsers = await m_MongoService.Users.Find(_ => true).ToListAsync(cancellationToken);
            var failedUsers = 0;

            foreach (var mongoUser in mongoUsers)
            {
                try
                {
                    var user = mongoUser.ToDto();
                    await m_UserRepo.CreateOrUpdateUserAsync(user);
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "Failed to migrate user {UserId}", mongoUser.Id);
                    failedUsers++;
                }
            }

            var mongoRelics = await m_MongoService.HsrRelics.Find(_ => true).ToListAsync(cancellationToken);
            var failedRelics = 0;

            foreach (var mongoRelic in mongoRelics)
            {
                try
                {
                    await m_RelicRepo.AddSetName(mongoRelic.SetId, mongoRelic.SetName);
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "Failed to migrate relic {SetId}", mongoRelic.SetId);
                    failedRelics++;
                }
            }

            await transaction.CommitAsync(cancellationToken);
            m_Logger.LogInformation("Migration completed. Users: {TotalUsers} (Failed: {FailedUsers}), Relics: {TotalRelics} (Failed: {FailedRelics})",
                mongoUsers.Count, failedUsers, mongoRelics.Count, failedRelics);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Migration failed and was rolled back");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
