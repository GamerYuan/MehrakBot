using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Extensions;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Npgsql;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
internal class MongoToSqlMigrator : IHostedService
{
    private readonly MongoDbService m_MongoService;
    private readonly IConfiguration m_Config;
    private readonly ILogger<MongoToSqlMigrator> m_Logger;

    public MongoToSqlMigrator(
        MongoDbService mongoService,
        IConfiguration config,
        ILogger<MongoToSqlMigrator> logger)
    {
        m_MongoService = mongoService;
        m_Config = config;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(m_Config["Postgres:ConnectionString"]);
        await connection.OpenAsync(cancellationToken);

        using var userDbContext = new UserDbContext(
            new DbContextOptionsBuilder<UserDbContext>()
                .UseNpgsql(connection)
                .Options);
        using var relicDbContext = new RelicDbContext(
            new DbContextOptionsBuilder<RelicDbContext>()
                .UseNpgsql(connection)
                .Options);

        m_Logger.LogInformation("Migrating MongoDB to SQL database");

        await using var transaction = await userDbContext.Database.BeginTransactionAsync(cancellationToken);
        await relicDbContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);

        try
        {
            int userCount = 0, failedUsers = 0, relicCount = 0, failedRelics = 0;

            if (!await userDbContext.Users.AnyAsync(cancellationToken: cancellationToken))
            {
                var mongoUsers = await m_MongoService.Users.Find(_ => true).ToListAsync(cancellationToken);
                userCount = mongoUsers.Count;

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
            }

            if (!await relicDbContext.HsrRelics.AnyAsync(cancellationToken: cancellationToken))
            {
                var mongoRelics = await m_MongoService.HsrRelics.Find(_ => true).ToListAsync(cancellationToken);
                relicCount = mongoRelics.Count;

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
            }

            await transaction.CommitAsync(cancellationToken);

            m_Logger.LogInformation("Migration completed. Users: {TotalUsers} (Failed: {FailedUsers}), Relics: {TotalRelics} (Failed: {FailedRelics})",
                    userCount, failedUsers, relicCount, failedRelics);
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
