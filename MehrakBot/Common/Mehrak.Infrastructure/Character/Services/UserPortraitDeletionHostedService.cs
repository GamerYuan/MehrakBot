using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Character.Services;

internal sealed class UserPortraitDeletionHostedService : BackgroundService
{
    private readonly UserPortraitDeletionProcessor m_Processor;
    private readonly ILogger<UserPortraitDeletionHostedService> m_Logger;

    public UserPortraitDeletionHostedService(
        UserPortraitDeletionProcessor processor,
        ILogger<UserPortraitDeletionHostedService> logger)
    {
        m_Processor = processor;
        m_Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await m_Processor.ProcessPendingDeletionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                m_Logger.LogError(exception, "Failed to process pending portrait deletions");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
