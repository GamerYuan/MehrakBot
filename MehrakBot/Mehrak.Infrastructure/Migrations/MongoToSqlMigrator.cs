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

        await m_MongoService.Users.Find(_ => true).ForEachAsync(async mongoUser =>
        {
            var user = mongoUser.ToDto();
            await m_UserRepo.CreateOrUpdateUserAsync(user);
        }, cancellationToken);

        await m_MongoService.HsrRelics.Find(_ => true).ForEachAsync(async mongoRelic =>
        {
            await m_RelicRepo.AddSetName(mongoRelic.SetId, mongoRelic.SetName);
        }, cancellationToken);

        m_Logger.LogInformation("Migration Completed");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
