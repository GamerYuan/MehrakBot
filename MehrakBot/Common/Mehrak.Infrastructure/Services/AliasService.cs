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

public class AliasService : IAliasService
{
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<AliasService> m_Logger;
    private readonly IConnectionMultiplexer m_Redis;
    private readonly string m_RedisInstanceName;

    private readonly Lock m_UpdateLock = new();

    public AliasService(
        IOptions<RedisConfig> redisConfig,
        IServiceScopeFactory serviceScopeFactory,
        IConnectionMultiplexer redis,
        ILogger<AliasService> logger)
    {
        m_RedisInstanceName = redisConfig.Value.InstanceName;
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
        m_Redis = redis;
    }

    private IDatabase Db => m_Redis.GetDatabase();

    private string GetAliasKey(Game game) => $"{m_RedisInstanceName}aliases:{game}";

    public Dictionary<string, string> GetAliases(Game gameName)
    {
        var key = GetAliasKey(gameName);
        var aliases = Db.HashGetAll(key);
        return aliases.ToDictionary(a => a.Name.ToString(), a => a.Value.ToString());
    }

    public async Task UpsertAliases(Game gameName, Dictionary<string, string> aliases)
    {
        try
        {
            if (aliases.Count == 0) return;

            var key = GetAliasKey(gameName);
            var cachedEntries = await Db.HashGetAllAsync(key);
            var cached = cachedEntries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var inserts = new Dictionary<string, string>();
            var updates = new Dictionary<string, string>();

            foreach (var kvp in aliases)
            {
                if (cached.TryGetValue(kvp.Key, out var existingChar))
                {
                    if (!existingChar.Equals(kvp.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        updates[kvp.Key] = kvp.Value;
                    }
                }
                else
                {
                    inserts[kvp.Key] = kvp.Value;
                }
            }

            if (inserts.Count == 0 && updates.Count == 0) return;

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            // Handle Inserts
            if (inserts.Count > 0)
            {
                foreach (var kvp in inserts)
                {
                    await characterContext.Aliases.AddAsync(new AliasModel
                    {
                        Game = gameName,
                        Alias = kvp.Key,
                        CharacterName = kvp.Value
                    });
                }
            }

            // Handle Updates
            if (updates.Count > 0)
            {
                var aliasesToUpdate = updates.Keys.ToList();
                var dbAliases = await characterContext.Aliases
                    .Where(x => x.Game == gameName && aliasesToUpdate.Contains(x.Alias))
                    .ToListAsync();

                foreach (var aliasModel in dbAliases)
                {
                    if (updates.TryGetValue(aliasModel.Alias, out var newCharName))
                    {
                        aliasModel.CharacterName = newCharName;
                    }
                }
            }

            await characterContext.SaveChangesAsync();

            // Update Redis
            var hashEntries = inserts.Concat(updates)
                .Select(kvp => new HashEntry(kvp.Key, kvp.Value))
                .ToArray();

            await Db.HashSetAsync(key, hashEntries);

            m_Logger.LogInformation("Upserted {Count} aliases for {Game}", inserts.Count + updates.Count, gameName);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while upserting aliases for {Game}", gameName);
        }
    }

    public async Task DeleteAlias(Game gameName, string alias)
    {
        try
        {
            var normalized = alias.ReplaceLineEndings("").Trim();

            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            var entity = await characterContext.Aliases
                .FirstOrDefaultAsync(x => x.Game == gameName && x.Alias == normalized);

            if (entity != null)
            {
                characterContext.Aliases.Remove(entity);
                await characterContext.SaveChangesAsync();

                // Remove from Redis
                var key = GetAliasKey(gameName);
                await Db.HashDeleteAsync(key, normalized);

                m_Logger.LogInformation("Deleted alias {Alias} for game {Game}", normalized, gameName);
            }
            else
            {
                m_Logger.LogInformation("Alias {Alias} not found for game {Game}; nothing to delete", normalized, gameName);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error occurred while deleting alias {Alias} for {Game}", alias, gameName);
        }
    }

    public async Task UpdateAllAliasesAsync()
    {
        m_UpdateLock.Enter();
        try
        {
            m_Logger.LogInformation("Starting character cache update for all games");

            var games = Enum.GetValues<Game>();
            var updateTasks = games.Select(UpdateAliasesAsync);

            await Task.WhenAll(updateTasks);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred during UpdateAllAliasesAsync");
        }
        finally
        {
            m_UpdateLock.Exit();
        }
    }

    private async Task UpdateAliasesAsync(Game gameName)
    {
        try
        {
            using var scope = m_ServiceScopeFactory.CreateScope();
            var characterContext = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

            m_Logger.LogDebug("Updating alias cache for {Game}", gameName);

            var aliases = await characterContext.Aliases.Where(a => a.Game == gameName)
                .ToDictionaryAsync(a => a.Alias, a => a.CharacterName);

            var key = GetAliasKey(gameName);
            var tran = Db.CreateTransaction();
            List<Task> transactions = [];
            transactions.Add(tran.KeyDeleteAsync(key));
            if (aliases.Count > 0)
            {
                var hashEntries = aliases.Select(a => new HashEntry(a.Key, a.Value)).ToArray();
                transactions.Add(tran.HashSetAsync(key, hashEntries));

                m_Logger.LogDebug("Upserted alias cache for {Game} with {Count} aliases", gameName, aliases.Count);
            }
            else
            {
                m_Logger.LogWarning("No aliases found for {Game} in database", gameName);
            }
            await tran.ExecuteAsync();
            await Task.WhenAll(transactions);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error occurred while updating alias cache for {Game}", gameName);
        }
    }
}
