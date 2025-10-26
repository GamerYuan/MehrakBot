#region

using System.Collections.Concurrent;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterCacheService : ICharacterCacheService
{
    private readonly ICharacterRepository m_CharacterRepository;
    private readonly IAliasRepository m_AliasRepository;
    private readonly ILogger<CharacterCacheService> m_Logger;
    private readonly ConcurrentDictionary<Game, List<string>> m_CharacterCache;
    private readonly Dictionary<Game, Dictionary<string, string>> m_AliasCache;
    private readonly SemaphoreSlim m_UpdateSemaphore;

    public CharacterCacheService(
        ICharacterRepository characterRepository,
        IAliasRepository aliasRepository,
        ILogger<CharacterCacheService> logger)
    {
        m_CharacterRepository = characterRepository;
        m_AliasRepository = aliasRepository;
        m_Logger = logger;
        m_AliasCache = [];
        m_CharacterCache = new ConcurrentDictionary<Game, List<string>>();
        m_UpdateSemaphore = new SemaphoreSlim(1, 1);
    }

    public List<string> GetCharacters(Game gameName)
    {
        if (m_CharacterCache.TryGetValue(gameName, out List<string>? characters))
        {
            m_Logger.LogDebug("Retrieved {Count} characters for {Game} from cache", characters.Count, gameName);
            return characters;
        }

        m_Logger.LogWarning("No cached characters found for {Game}, returning empty list", gameName);

        _ = Task.Run(async () => await UpdateCharactersAsync(gameName));

        return [];
    }

    public Dictionary<string, string> GetAliases(Game gameName)
    {
        return m_AliasCache.TryGetValue(gameName, out Dictionary<string, string>? dict) ? dict : [];
    }

    public async Task UpdateAllCharactersAsync()
    {
        await m_UpdateSemaphore.WaitAsync();
        try
        {
            m_Logger.LogInformation("Starting character cache update for all games");

            Game[] games = Enum.GetValues<Game>();
            IEnumerable<Task> updateTasks = games.Select(UpdateCharactersAsync);
            IEnumerable<Task> aliasTasks = games.Select(UpdateAliasesAsync);

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

            List<string> characters = await m_CharacterRepository.GetCharactersAsync(gameName);
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

            var aliases = new Dictionary<string, string>(await m_AliasRepository.GetAliasesAsync(gameName),
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