#region

using System.Collections.Concurrent;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterCacheService : ICharacterCacheService
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<CharacterCacheService> m_Logger;
    private readonly ConcurrentDictionary<Game, List<string>> m_CharacterCache;
    private readonly Dictionary<Game, Dictionary<string, string>> m_AliasCache;
    private readonly SemaphoreSlim m_UpdateSemaphore;

    public CharacterCacheService(
        IServiceScopeFactory scopeFactory,
        ILogger<CharacterCacheService> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
        m_AliasCache = [];
        m_CharacterCache = new ConcurrentDictionary<Game, List<string>>();
        m_UpdateSemaphore = new SemaphoreSlim(1, 1);
    }

    public List<string> GetCharacters(Game gameName)
    {
        if (m_CharacterCache.TryGetValue(gameName, out var characters))
        {
            m_Logger.LogDebug("Retrieved {Count} characters for {Game} from cache", characters.Count, gameName);
            return characters;
        }

        m_Logger.LogWarning("No cached characters found for {Game}, returning empty list", gameName);

        return [];
    }

    public Dictionary<string, string> GetAliases(Game gameName)
    {
        return m_AliasCache.TryGetValue(gameName, out var dict) ? dict : [];
    }

    public async Task UpsertCharacters(Game gameName, IEnumerable<string> characters)
    {
        try
        {
            using var scope = m_ScopeFactory.CreateScope();
            var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            var toAdd = characters.Except(m_CharacterCache.GetValueOrDefault(gameName, [])).ToList();

            if (toAdd.Count == 0) return;

            await characterRepository.UpsertCharactersAsync(gameName, toAdd);
            await UpdateCharactersAsync(gameName);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while upserting characters for {Game}", gameName);
        }
    }

    public async Task UpdateAllCharactersAsync()
    {
        await m_UpdateSemaphore.WaitAsync();
        try
        {
            m_Logger.LogInformation("Starting character cache update for all games");

            var games = Enum.GetValues<Game>();
            var updateTasks = games.Select(UpdateCharactersAsync);
            var aliasTasks = games.Select(UpdateAliasesAsync);

            await Task.WhenAll(updateTasks);
            await Task.WhenAll(aliasTasks);

            m_Logger.LogInformation("Completed character and alias cache update for all games");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character and alias cache for all games");
        }
        finally
        {
            m_UpdateSemaphore.Release();
        }
    }

    public async Task UpdateCharactersAsync(Game gameName)
    {
        try
        {
            m_Logger.LogDebug("Updating character cache for {Game}", gameName);

            using var scope = m_ScopeFactory.CreateScope();
            var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();

            var characters = await characterRepository.GetCharactersAsync(gameName);
            characters.Sort();

            if (characters.Count > 0)
            {
                m_CharacterCache.AddOrUpdate(gameName, characters, (_, _) => characters);
                m_Logger.LogDebug("Updated character cache for {Game} with {Count} characters", gameName,
                    characters.Count);
            }
            else
            {
                m_Logger.LogWarning("No characters found for {Game} in database", gameName);
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character cache for {Game}", gameName);
        }
    }

    private async Task UpdateAliasesAsync(Game gameName)
    {
        try
        {
            m_Logger.LogDebug("Updating alias cache for {Game}", gameName);

            using var scope = m_ScopeFactory.CreateScope();
            var aliasRepository = scope.ServiceProvider.GetRequiredService<IAliasRepository>();

            var aliases = new Dictionary<string, string>(await aliasRepository.GetAliasesAsync(gameName),
                StringComparer.OrdinalIgnoreCase);

            if (aliases.Count > 0)
            {
                if (!m_AliasCache.TryAdd(gameName, aliases)) m_AliasCache[gameName] = aliases;

                m_Logger.LogDebug("Updated alias cache for {Game} with {Count} aliases", gameName, aliases.Count);
            }
            else
            {
                m_Logger.LogWarning("No aliases found for {Game} in database", gameName);
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating alias cache for {Game}", gameName);
        }
    }

    public Dictionary<Game, int> GetCacheStatus()
    {
        return m_CharacterCache.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count);
    }

    public void ClearCache()
    {
        m_Logger.LogInformation("Clearing character cache for all games");
        m_CharacterCache.Clear();
    }

    public void ClearCache(Game gameName)
    {
        if (m_CharacterCache.TryRemove(gameName, out _))
            m_Logger.LogInformation("Cleared character cache for {Game}", gameName);
    }
}
