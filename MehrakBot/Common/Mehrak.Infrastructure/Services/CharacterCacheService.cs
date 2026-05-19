#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

#endregion

namespace Mehrak.Infrastructure.Services;

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

            var byName = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, serverId) in normalised)
            {
                if (serverId.HasValue || !byName.TryGetValue(name, out var value))
                    byName[name] = serverId;
                else if (!value.HasValue && serverId.HasValue)
                    byName[name] = serverId;
            }

            if (byName.Count == 0) return;

            var key = GetCharacterKey(gameName);

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            var existing = await characterContext.Characters
                .Where(x => x.Game == gameName && byName.Keys.Contains(x.Name))
                .Include(x => x.ServerIds)
                .ToDictionaryAsync(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

            var newNames = new List<string>();

            foreach (var (name, serverId) in byName.OrderBy(x => x.Key))
            {
                if (existing.TryGetValue(name, out var character))
                {
                    if (serverId.HasValue && character.ServerIds.All(s => s.ServerId != serverId.Value))
                    {
                        character.ServerIds.Add(new CharacterServerIdModel { ServerId = serverId.Value });
                    }
                }
                else
                {
                    var newChar = new CharacterModel
                    {
                        Game = gameName,
                        Name = name
                    };

                    if (serverId.HasValue)
                        newChar.ServerIds.Add(new CharacterServerIdModel { ServerId = serverId.Value });

                    await characterContext.Characters.AddAsync(newChar);
                    newNames.Add(name);
                }
            }

            await characterContext.SaveChangesAsync();

            var allNames = existing.Keys.Concat(newNames).Distinct().OrderBy(x => x).Select(x => (RedisValue)x);
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
