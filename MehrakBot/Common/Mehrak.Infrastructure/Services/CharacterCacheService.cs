#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

#endregion

namespace Mehrak.Infrastructure.Services;

public class CharacterCacheService : ICharacterCacheService
{
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<CharacterCacheService> m_Logger;
    private readonly IConnectionMultiplexer m_Redis;
    private readonly IAliasService m_AliasService;
    private readonly SemaphoreSlim m_UpdateSemaphore;
    private const string InstanceName = "Mehrak_";

    public CharacterCacheService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<CharacterCacheService> logger,
        IConnectionMultiplexer redis,
        IAliasService aliasService)
    {
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
        m_Redis = redis;
        m_AliasService = aliasService;
        m_UpdateSemaphore = new SemaphoreSlim(1, 1);
    }

    private IDatabase Db => m_Redis.GetDatabase();

    private static string GetCharacterKey(Game game) => $"{InstanceName}characters:{game}";

    public List<string> GetCharacters(Game gameName)
    {
        var key = GetCharacterKey(gameName);
        var characters = Db.SetMembers(key);

        if (characters.Length > 0)
        {
            m_Logger.LogDebug("Retrieved {Count} characters for {Game} from cache", characters.Length, gameName);
            return [.. characters.Select(c => c.ToString())];
        }

        m_Logger.LogWarning("No cached characters found for {Game}, returning empty list", gameName);

        return [];
    }

    public async Task UpsertCharacters(Game gameName, IEnumerable<string> characters)
    {
        try
        {
            var incoming = new HashSet<string>(characters, StringComparer.OrdinalIgnoreCase);
            var key = GetCharacterKey(gameName);
            var cachedMembers = await Db.SetMembersAsync(key);
            var cached = cachedMembers.Select(x => x.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toAdd = incoming.Except(cached).ToList();

            if (toAdd.Count == 0) return;

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            foreach (var newChar in toAdd)
            {
                await characterContext.Characters.AddAsync(new CharacterModel()
                {
                    Game = gameName,
                    Name = newChar
                });
            }

            await characterContext.SaveChangesAsync();
            await Db.SetAddAsync(key, toAdd.Select(x => (RedisValue)x).ToArray());

            m_Logger.LogInformation("Updated {Count} names for {Game}", toAdd.Count, gameName);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while upserting characters for {Game}", gameName);
        }
    }
    public async Task DeleteCharacter(Game gameName, string characterName)
    {
        try
        {
            var normalized = characterName.ReplaceLineEndings("").Trim();

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            var entity = await characterContext.Characters
                .FirstOrDefaultAsync(x => x.Game == gameName && x.Name == normalized);

            if (entity != null)
            {
                characterContext.Characters.Remove(entity);
                await characterContext.SaveChangesAsync();

                // Remove from Redis
                var key = GetCharacterKey(gameName);
                await Db.SetRemoveAsync(key, normalized);

                m_Logger.LogInformation("Deleted character {Character} from game {Game}", normalized, gameName);
            }
            else
            {
                m_Logger.LogInformation("Character {Character} not found for game {Game}; nothing to delete", normalized, gameName);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error occurred while deleting character {Character} for {Game}", characterName, gameName);
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
            var aliasTasks = games.Select(m_AliasService.UpdateAliasesAsync);

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
            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            m_Logger.LogDebug("Updating character cache for {Game}", gameName);

            var characters = await characterContext.Characters.AsNoTracking()
                .Where(x => x.Game == gameName)
                .Select(x => x.Name)
                .OrderBy(x => x)
                .ToListAsync();

            if (characters.Count > 0)
            {
                var key = GetCharacterKey(gameName);
                await Db.KeyDeleteAsync(key);
                await Db.SetAddAsync(key, [.. characters.Select(x => (RedisValue)x)]);

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

    public Dictionary<Game, int> GetCacheStatus()
    {
        var status = new Dictionary<Game, int>();
        foreach (var game in Enum.GetValues<Game>())
        {
            var count = Db.SetLength(GetCharacterKey(game));
            if (count > 0) status[game] = (int)count;
        }

        return status;
    }

    public void ClearCache()
    {
        m_Logger.LogInformation("Clearing character cache for all games");
        var db = Db;
        foreach (var game in Enum.GetValues<Game>())
        {
            db.KeyDelete(GetCharacterKey(game));
        }
    }

    public void ClearCache(Game gameName)
    {
        var db = Db;
        db.KeyDelete(GetCharacterKey(gameName));
        m_Logger.LogInformation("Cleared character cache for {Game}", gameName);
    }
}
