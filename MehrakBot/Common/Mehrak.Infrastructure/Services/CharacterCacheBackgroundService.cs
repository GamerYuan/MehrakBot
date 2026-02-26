#region

using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterCacheBackgroundService : IHostedService
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly IAliasService m_AliasService;
    private readonly ILogger<CharacterCacheBackgroundService> m_Logger;
    private readonly CharacterCacheConfig m_Config;

    private readonly CancellationTokenSource m_Cts = new();

    public CharacterCacheBackgroundService(
        ICharacterCacheService characterCacheService,
        IAliasService aliasService,
        ILogger<CharacterCacheBackgroundService> logger,
        IOptions<CharacterCacheConfig> config)
    {
        m_CharacterCacheService = characterCacheService;
        m_AliasService = aliasService;
        m_Logger = logger;
        m_Config = config.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (m_Config.EnableInitialPopulation)
            await PerformInitialUpdate(cancellationToken);

        if (m_Config.EnablePeriodicUpdates)
            _ = PerformPeriodicUpdateAsync(m_Cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_Cts.Cancel();
        m_Cts.Dispose();
        return Task.CompletedTask;
    }

    private async Task PerformInitialUpdate(CancellationToken cancellationToken)
    {
        try
        {
            m_Logger.LogInformation("Performing initial character cache population");
            await m_CharacterCacheService.UpdateAllCharactersAsync();
            await m_AliasService.UpdateAllAliasesAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogInformation("Initial character cache population canceled");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during initial character cache population");
        }
    }

    private async Task PerformPeriodicUpdateAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(m_Config.UpdateInterval, token);
                token.ThrowIfCancellationRequested();
                await m_CharacterCacheService.UpdateAllCharactersAsync();
                await m_AliasService.UpdateAllAliasesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogInformation("Periodic character cache update canceled");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during periodic character cache update");
        }
    }
}
