#region

using System.Collections.Concurrent;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public class CharacterCacheService : ICharacterCacheService
{
    private readonly ICharacterRepository m_CharacterRepository;
    private readonly ILogger<CharacterCacheService> m_Logger;
    private readonly ConcurrentDictionary<GameName, List<string>> m_CharacterCache;
    private readonly SemaphoreSlim m_UpdateSemaphore;

    public CharacterCacheService(ICharacterRepository characterRepository, ILogger<CharacterCacheService> logger)
    {
        m_CharacterRepository = characterRepository;
        m_Logger = logger;
        m_CharacterCache = new ConcurrentDictionary<GameName, List<string>>();
        m_UpdateSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Gets the cached character list for the specified game.
    /// If not cached, returns an empty list and triggers a background update.
    /// </summary>
    /// <param name="gameName">The game to get characters for</param>
    /// <returns>List of character names</returns>
    public List<string> GetCharacters(GameName gameName)
    {
        if (m_CharacterCache.TryGetValue(gameName, out var characters))
        {
            m_Logger.LogDebug("Retrieved {Count} characters for {GameName} from cache", characters.Count, gameName);
            return characters;
        }

        m_Logger.LogWarning("No cached characters found for {GameName}, returning empty list", gameName);

        // Trigger a background update for this game
        _ = Task.Run(async () => await UpdateCharactersAsync(gameName));

        return [];
    }

    /// <summary>
    /// Updates the character cache for all supported games.
    /// </summary>
    /// <returns>Task representing the update operation</returns>
    public async Task UpdateAllCharactersAsync()
    {
        await m_UpdateSemaphore.WaitAsync();
        try
        {
            m_Logger.LogInformation("Starting character cache update for all games");

            var games = Enum.GetValues<GameName>();
            var updateTasks = games.Select(UpdateCharactersAsync);

            await Task.WhenAll(updateTasks);

            m_Logger.LogInformation("Completed character cache update for all games");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character cache for all games");
        }
        finally
        {
            m_UpdateSemaphore.Release();
        }
    }

    /// <summary>
    /// Updates the character cache for a specific game.
    /// </summary>
    /// <param name="gameName">The game to update characters for</param>
    /// <returns>Task representing the update operation</returns>
    public async Task UpdateCharactersAsync(GameName gameName)
    {
        try
        {
            m_Logger.LogDebug("Updating character cache for {GameName}", gameName);

            var characters = await m_CharacterRepository.GetCharactersAsync(gameName);
            characters.Sort();

            if (characters.Count > 0)
            {
                m_CharacterCache.AddOrUpdate(gameName, characters, (_, _) => characters);
                m_Logger.LogDebug("Updated character cache for {GameName} with {Count} characters",
                    gameName, characters.Count);
            }
            else
            {
                m_Logger.LogWarning("No characters found for {GameName} in database", gameName);
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character cache for {GameName}", gameName);
        }
    }

    /// <summary>
    /// Gets the current cache status for all games.
    /// </summary>
    /// <returns>Dictionary with game names and character counts</returns>
    public Dictionary<GameName, int> GetCacheStatus()
    {
        return m_CharacterCache.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count
        );
    }

    /// <summary>
    /// Clears the cache for all games.
    /// </summary>
    public void ClearCache()
    {
        m_Logger.LogInformation("Clearing character cache for all games");
        m_CharacterCache.Clear();
    }

    /// <summary>
    /// Clears the cache for a specific game.
    /// </summary>
    /// <param name="gameName">The game to clear cache for</param>
    public void ClearCache(GameName gameName)
    {
        if (m_CharacterCache.TryRemove(gameName, out _))
            m_Logger.LogInformation("Cleared character cache for {GameName}", gameName);
    }
}
