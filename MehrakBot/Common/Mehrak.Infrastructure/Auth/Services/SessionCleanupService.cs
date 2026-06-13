using Mehrak.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Auth.Services;

public class SessionCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<SessionCleanupService> m_Logger;

    public SessionCleanupService(IServiceScopeFactory scopeFactory, ILogger<SessionCleanupService> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CleanupInterval, stoppingToken);

            try
            {
                using var scope = m_ScopeFactory.CreateScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<IDashboardSessionService>();
                var count = await sessionService.CleanupExpiredSessionsAsync(stoppingToken);
                _ = count;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Session cleanup failed");
            }
        }
    }
}
