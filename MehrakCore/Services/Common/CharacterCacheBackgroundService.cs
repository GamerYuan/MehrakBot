#region

using MehrakCore.Config;
using MehrakCore.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#endregion

namespace MehrakCore.Services.Common;

public class CharacterCacheBackgroundService : BackgroundService
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private readonly ILogger<CharacterCacheBackgroundService> m_Logger;
    private readonly CharacterCacheConfig m_Config;

    public CharacterCacheBackgroundService(
        ICharacterCacheService characterCacheService,
        ILogger<CharacterCacheBackgroundService> logger,
        IOptions<CharacterCacheConfig> config)
    {
        m_CharacterCacheService = characterCacheService;
        m_Logger = logger;
        m_Config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!m_Config.EnablePeriodicUpdates)
        {
            m_Logger.LogInformation("Character cache background service disabled via configuration");
            return;
        }

        m_Logger.LogInformation("Character cache background service started with update interval of {UpdateInterval}",
            m_Config.UpdateInterval);

        // Perform an initial update to populate the cache
        if (m_Config.EnableInitialPopulation)
        {
            await PerformInitialUpdate(stoppingToken);
        }

        // Start the periodic update loop
        await PeriodicUpdateLoop(stoppingToken);
    }

    private async Task PerformInitialUpdate(CancellationToken stoppingToken)
    {
        try
        {
            m_Logger.LogInformation("Performing initial character cache population");
            await m_CharacterCacheService.UpdateAllCharactersAsync();

            var cacheStatus = m_CharacterCacheService.GetCacheStatus();
            m_Logger.LogInformation("Initial character cache populated: {CacheStatus}",
                string.Join(", ", cacheStatus.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during initial character cache population");
        }
    }

    private async Task PeriodicUpdateLoop(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(m_Config.UpdateInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await PerformPeriodicUpdate();
            }
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogInformation("Character cache background service stopped");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Unexpected error in character cache background service");
        }
    }

    private async Task PerformPeriodicUpdate()
    {
        try
        {
            m_Logger.LogDebug("Performing periodic character cache update");

            var beforeStatus = m_CharacterCacheService.GetCacheStatus();
            await m_CharacterCacheService.UpdateAllCharactersAsync();
            var afterStatus = m_CharacterCacheService.GetCacheStatus();

            // Log changes if any
            LogCacheChanges(beforeStatus, afterStatus);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during periodic character cache update");
        }
    }

    private void LogCacheChanges(Dictionary<GameName, int> beforeStatus, Dictionary<GameName, int> afterStatus)
    {
        var changes = new List<string>();

        foreach (var (game, afterCount) in afterStatus)
        {
            if (beforeStatus.TryGetValue(game, out var beforeCount))
            {
                if (beforeCount != afterCount)
                {
                    changes.Add($"{game}: {beforeCount} -> {afterCount}");
                }
            }
            else
            {
                changes.Add($"{game}: new -> {afterCount}");
            }
        }

        // Check for removed games
        foreach (var (game, beforeCount) in beforeStatus)
        {
            if (!afterStatus.ContainsKey(game))
            {
                changes.Add($"{game}: {beforeCount} -> removed");
            }
        }

        if (changes.Count > 0)
        {
            m_Logger.LogInformation("Character cache changes detected: {Changes}", string.Join(", ", changes));
        }
        else
        {
            m_Logger.LogDebug("No character cache changes detected during periodic update");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        m_Logger.LogInformation("Character cache background service stopping");
        await base.StopAsync(stoppingToken);
    }
}
