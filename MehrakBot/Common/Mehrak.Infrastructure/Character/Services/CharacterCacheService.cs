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
    private const int CacheRefreshAttempts = 3;

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
        try
        {
            var characters = Db.SetMembers(GetCharacterKey(gameName));
            if (characters.Length > 0)
            {
                m_Logger.LogDebug("Retrieved {Count} characters for {Game} from cache", characters.Length, gameName);
                return [.. characters.Select(character => character.ToString())];
            }
        }
        catch (Exception exception)
        {
            m_Logger.LogWarning(exception, "Character cache read failed for {Game}; using PostgreSQL", gameName);
        }

        // Reads must not queue behind writers or retry an unavailable cache.
        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        return context.Characters.AsNoTracking()
            .Where(character => character.Game == gameName)
            .OrderBy(character => character.Name)
            .Select(character => character.Name)
            .ToList();
    }

    public Task UpsertCharacters(Game gameName, IEnumerable<string> characters) =>
        UpsertCharacters(gameName, characters.Select(name => new CharacterUpsertEntry(name)));

    public async Task UpsertCharacters(Game gameName, IEnumerable<CharacterUpsertEntry> entries)
    {
        var normalised = entries
            .Select(entry => new CharacterUpsertEntry(entry.Name.ReplaceLineEndings("").Trim(), entry.ServerId))
            .Where(entry => !string.IsNullOrEmpty(entry.Name));

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

        if (byName.Count == 0)
            return;

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await characterContext.Database.BeginTransactionAsync();
        await CharacterDbLock.AcquireAsync(characterContext, $"characters:{gameName}");

        var existingDb = await characterContext.Characters
            .Where(character => character.Game == gameName)
            .Include(character => character.ServerIds)
            .ToListAsync();

        var existing = existingDb
            .DistinctBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .Where(character => byName.ContainsKey(character.Name))
            .ToDictionary(character => character.Name, StringComparer.OrdinalIgnoreCase);

        var newNames = new List<string>();
        foreach (var (name, serverIds) in byName.OrderBy(entry => entry.Key))
        {
            if (existing.TryGetValue(name, out var character))
            {
                var existingIds = character.ServerIds.Select(serverId => serverId.ServerId).ToHashSet();
                foreach (var serverId in serverIds.Where(serverId => !existingIds.Contains(serverId)))
                    character.ServerIds.Add(new CharacterServerIdModel { ServerId = serverId });
            }
            else
            {
                var newCharacter = new CharacterModel { Game = gameName, Name = name };
                foreach (var serverId in serverIds)
                    newCharacter.ServerIds.Add(new CharacterServerIdModel { ServerId = serverId });

                await characterContext.Characters.AddAsync(newCharacter);
                newNames.Add(name);
            }
        }

        await characterContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await RefreshCacheFromDatabaseAsync(gameName);

        if (newNames.Count > 0)
            m_Logger.LogInformation("Added {Count} names for {Game}", newNames.Count, gameName);
    }

    public async Task DeleteCharacter(Game gameName, string characterName)
    {
        var normalized = characterName.ReplaceLineEndings("").Trim();

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await characterContext.Database.BeginTransactionAsync();
        await CharacterDbLock.AcquireAsync(characterContext, $"characters:{gameName}");

        var entity = await characterContext.Characters
            .FirstOrDefaultAsync(character => character.Game == gameName && character.Name == normalized);

        if (entity == null)
        {
            m_Logger.LogInformation("Character {Character} not found for game {Game}; nothing to delete", normalized, gameName);
            await transaction.CommitAsync();
            await RefreshCacheFromDatabaseAsync(gameName);
            return;
        }

        characterContext.Characters.Remove(entity);
        await characterContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await RefreshCacheFromDatabaseAsync(gameName);

        m_Logger.LogInformation("Deleted character {Character} from game {Game}", normalized, gameName);
    }

    public async Task UpdateAllCharactersAsync()
    {
        m_Logger.LogInformation("Starting character cache update for all games");
        await Task.WhenAll(Enum.GetValues<Game>().Select(UpdateCharactersAsync));
        m_Logger.LogInformation("Completed character and alias cache update for all games");
    }

    public async Task UpdateCharactersAsync(Game gameName)
    {
        await RefreshCacheFromDatabaseAsync(gameName);
    }

    private async Task<List<string>> RefreshCacheFromDatabaseAsync(Game gameName)
    {
        using var scope = m_ServiceScopeFactory.CreateScope();
        using var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await characterContext.Database.BeginTransactionAsync();
        await CharacterDbLock.AcquireAsync(characterContext, $"characters:{gameName}");

        var characters = await characterContext.Characters
            .AsNoTracking()
            .Where(character => character.Game == gameName)
            .Select(character => character.Name)
            .OrderBy(name => name)
            .ToListAsync();

        await RefreshCacheAsync(gameName, characters);

        await transaction.CommitAsync();
        return characters;
    }

    private async Task RefreshCacheAsync(Game gameName, IReadOnlyCollection<string> characters)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= CacheRefreshAttempts; attempt++)
        {
            try
            {
                var transaction = Db.CreateTransaction();
                var queuedCommands = new List<Task>
                {
                    transaction.KeyDeleteAsync(GetCharacterKey(gameName))
                };
                if (characters.Count > 0)
                {
                    queuedCommands.Add(transaction.SetAddAsync(
                        GetCharacterKey(gameName), [.. characters.Select(name => (RedisValue)name)]));
                }

                if (await transaction.ExecuteAsync())
                {
                    await Task.WhenAll(queuedCommands);
                    return;
                }

                throw new RedisException("Redis transaction was not committed.");
            }
            catch (Exception exception) when (attempt < CacheRefreshAttempts)
            {
                lastException = exception;
                m_Logger.LogWarning(exception, "Character cache refresh attempt {Attempt} failed for {Game}", attempt, gameName);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        try
        {
            await Db.KeyDeleteAsync(GetCharacterKey(gameName));
        }
        catch (Exception invalidationException)
        {
            lastException = new AggregateException(lastException!, invalidationException);
        }

        throw new CacheSynchronizationException(
            $"Database committed for characters in {gameName}, but the Redis cache could not be refreshed.",
            lastException!);
    }
}
