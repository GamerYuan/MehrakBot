#region

using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Bot.Services;

public class AsyncInitializationHostedService : IHostedService
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly ILogger<AsyncInitializationHostedService> m_Logger;

    public AsyncInitializationHostedService(IServiceProvider serviceProvider,
        ILogger<AsyncInitializationHostedService> logger)
    {
        m_ServiceProvider = serviceProvider;
        m_Logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = m_ServiceProvider.CreateScope();

        var services = scope.ServiceProvider.GetServices<IAsyncInitializable>();

        await Task.WhenAll(services.Select(x => x.InitializeAsync(cancellationToken)));

        m_Logger.LogInformation("Initialized {Count} services", services.Count());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}