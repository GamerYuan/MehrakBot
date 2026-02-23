using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mehrak.Bot.Services;

internal class UserTrackerBackfillService : IHostedService
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly UserCountTrackerService m_UserTracker;
    private readonly ILogger<UserTrackerBackfillService> m_Logger;

    public UserTrackerBackfillService(IServiceScopeFactory scopeFactory, UserCountTrackerService userTracker,
        ILogger<UserTrackerBackfillService> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_UserTracker = userTracker;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var curr = await m_UserTracker.GetUserCountAsync();
        if (curr > 0) return;

        using var scope = m_ScopeFactory.CreateScope();
        using var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        var count = await userContext.Users.CountAsync(cancellationToken);
        m_Logger.LogInformation("Backfilling user count with {Count} users", count);
        await m_UserTracker.AdjustUserCountAsync(count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
