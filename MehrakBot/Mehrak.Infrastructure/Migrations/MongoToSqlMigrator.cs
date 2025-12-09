using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
internal class MongoToSqlMigrator : IHostedService
{
    private readonly MongoDbService m_MongoService;
    private readonly UserDbContext m_DbContext;
    private readonly IUserRepository m_UserRepo;
    private readonly IRelicRepository m_RelicRepo;

    public MongoToSqlMigrator(
        MongoDbService mongoService,
        UserDbContext dbContext,
        IUserRepository userRepo,
        IRelicRepository relicRepo)
    {
        m_MongoService = mongoService;
        m_DbContext = dbContext;
        m_UserRepo = userRepo;
        m_RelicRepo = relicRepo;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await m_DbContext.Users.AnyAsync(cancellationToken: cancellationToken)) return;

        await m_MongoService.Users.Find(_ => true).ForEachAsync(async mongoUser =>
        {
            var user = mongoUser.ToDto();
            await m_UserRepo.CreateOrUpdateUserAsync(user);
        }, cancellationToken);

        await m_MongoService.HsrRelics.Find(_ => true).ForEachAsync(async mongoRelic =>
        {
            await m_RelicRepo.AddSetName(mongoRelic.SetId, mongoRelic.SetName);
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
