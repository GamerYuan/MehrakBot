using System.Diagnostics;
using Mehrak.Infrastructure.Auth;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.CodeRedeem;
using Mehrak.Infrastructure.Documentation;
using Mehrak.Infrastructure.ReleaseNote;
using Mehrak.Infrastructure.Relic;
using Mehrak.Infrastructure.User;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OpenTelemetry.Trace;

namespace Mehrak.MigrationService;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity(
            "Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            var dbContexts = new DbContext[]
            {
                scopedServices.GetRequiredService<CharacterDbContext>(),
                scopedServices.GetRequiredService<UserDbContext>(),
                scopedServices.GetRequiredService<RelicDbContext>(),
                scopedServices.GetRequiredService<CodeRedeemDbContext>(),
                scopedServices.GetRequiredService<DashboardAuthDbContext>(),
                scopedServices.GetRequiredService<DocumentationDbContext>(),
                scopedServices.GetRequiredService<ReleaseNoteDbContext>()
            };

            foreach (var dbContext in dbContexts)
            {
                var dbActivity = ActivitySource.StartActivity(
                    $"Migrating {dbContext.GetType().Name}", ActivityKind.Client);
                try
                {
                    await RunMigrationAsync(dbContext, cancellationToken);
                    dbActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    dbActivity?.AddException(ex);
                    dbActivity?.SetStatus(ActivityStatusCode.Error);
                    throw;
                }
                finally
                {
                    dbActivity?.Dispose();
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RunMigrationAsync(
        DbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Run migration in a transaction to avoid partial migration if it fails.
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}