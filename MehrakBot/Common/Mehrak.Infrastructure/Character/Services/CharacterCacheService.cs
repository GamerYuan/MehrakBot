#region

using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#endregion

namespace Mehrak.Infrastructure.Character.Services;

public class CharacterCacheService : ICharacterCacheService
{
    private readonly string m_RedisInstanceName;
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<CharacterCacheService> m_Logger;
    private readonly IConnectionMultiplexer m_Redis;

    public CharacterCacheService(
        IOptions<RedisConfig> redisConfig,
        IServiceScopeFactory serviceScopeFactory,
        IConnectionMultiplexer redis,
        ILogger<CharacterCacheService> logger)

    {
        m_RedisInstanceName = redisConfig.Value.InstanceName;
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
        m_Redis = redis;
    }

    private IDatabase Db => m_Redis.GetDatabase();

    private string GetCharacterKey(Game game) => $"{m_RedisInstanceName}characters:{game}";

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
        await UpsertCharacters(gameName,
            characters.Select(name => new CharacterUpsertEntry(name)));
    }

    public async Task UpsertCharacters(Game gameName, IEnumerable<CharacterUpsertEntry> entries)
    {
        try
        {
            var normalised = entries
                .Select(e => new CharacterUpsertEntry(
                    e.Name.ReplaceLineEndings("").Trim(),
                    e.ServerId))
                .Where(e => !string.IsNullOrEmpty(e.Name));

            var byName = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, serverId) in normalised)
            {
                if (!byName.TryGetValue(name, out var serverIds))
                {
                    serverIds = [];
                    byName[name] = serverIds;
                }

                if (serverId.HasValue)
                    serverIds.Add(serverId.Value);
            }

            if (byName.Count == 0) return;

            var key = GetCharacterKey(gameName);

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            var existingDb = await characterContext.Characters
                .Where(x => x.Game == gameName)
                .Include(x => x.ServerIds)
                .ToListAsync();

            var existing = existingDb
                .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Where(x => byName.ContainsKey(x.Name))
                .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

            var newNames = new List<string>();

            foreach (var (name, serverIds) in byName.OrderBy(x => x.Key))
            {
                if (existing.TryGetValue(name, out var character))
                {
                    var existingIds = character.ServerIds.Select(s => s.ServerId).ToHashSet();
                    foreach (var sid in serverIds.Where(sid => !existingIds.Contains(sid)))
                        character.ServerIds.Add(new CharacterServerIdModel { ServerId = sid });
                }
                else
                {
                    var newChar = new CharacterModel
                    {
                        Game = gameName,
                        Name = name
                    };

                    foreach (var serverId in serverIds)
                        newChar.ServerIds.Add(new CharacterServerIdModel { ServerId = serverId });

                    await characterContext.Characters.AddAsync(newChar);
                    newNames.Add(name);
                }
            }

            await characterContext.SaveChangesAsync();


            var allNames = existingDb.Select(x => x.Name).Concat(newNames).Distinct().OrderBy(x => x).Select(x => (RedisValue)x);
            await Db.SetAddAsync(key, [.. allNames]);

            if (newNames.Count > 0)
                m_Logger.LogInformation("Added {Count} names for {Game}", newNames.Count, gameName);
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
        try
        {
            m_Logger.LogInformation("Starting character cache update for all games");

            var games = Enum.GetValues<Game>();
            var updateTasks = games.Select(UpdateCharactersAsync);

            await Task.WhenAll(updateTasks);

            m_Logger.LogInformation("Completed character and alias cache update for all games");
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character and alias cache for all games");
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

            var key = GetCharacterKey(gameName);
            var tran = Db.CreateTransaction();
            List<Task> transactions = [];
            transactions.Add(tran.KeyDeleteAsync(key));

            if (characters.Count > 0)
            {
                transactions.Add(tran.SetAddAsync(key, [.. characters.Select(x => (RedisValue)x)]));

                m_Logger.LogDebug("Updated character cache for {Game} with {Count} characters", gameName,
                    characters.Count);
            }
            else
            {
                m_Logger.LogWarning("No characters found for {Game} in database", gameName);
            }
            await tran.ExecuteAsync();
            await Task.WhenAll(transactions);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating character cache for {Game}", gameName);
        }
    }
}
