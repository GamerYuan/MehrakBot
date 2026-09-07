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

public class AliasService : IAliasService
{
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<AliasService> m_Logger;
    private readonly IConnectionMultiplexer m_Redis;
    private readonly string m_RedisInstanceName;

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
        // PostgreSQL is authoritative. Redis is a derived lookup cache and must
        // never decide whether an alias can be created or which target it has.
        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var aliases = context.Aliases
            .AsNoTracking()
            .Where(alias => alias.Game == gameName)
            .OrderBy(alias => alias.Id)
            .ToList();

        return aliases
            .GroupBy(alias => AliasModel.NormalizeAlias(alias.Alias), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().CharacterName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task ReconcileAliasesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await CharacterDbLock.AcquireAsync(context, "aliases:all", cancellationToken);

        var aliases = await context.Aliases
            .OrderBy(alias => alias.Game)
            .ThenBy(alias => alias.Id)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var group in aliases.GroupBy(alias => (alias.Game, Alias: AliasModel.NormalizeAlias(alias.Alias))))
        {
            var winner = group.First();
            winner.Alias = group.Key.Alias;
            changed = true;

            foreach (var duplicate in group.Skip(1))
            {
                if (!string.Equals(winner.CharacterName, duplicate.CharacterName, StringComparison.OrdinalIgnoreCase))
                {
                    context.AliasConflicts.Add(new AliasConflictModel
                    {
                        Game = duplicate.Game,
                        Alias = group.Key.Alias,
                        OriginalAlias = duplicate.Alias,
                        CharacterName = duplicate.CharacterName,
                        SourceAliasId = duplicate.Id
                    });
                }

                context.Aliases.Remove(duplicate);
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertAliases(Game gameName, Dictionary<string, string> aliases)
    {
        if (aliases.Count == 0)
            return;

        var normalized = aliases
            .Select(entry => (Alias: AliasModel.NormalizeAlias(entry.Key), CharacterName: entry.Value.ReplaceLineEndings("").Trim()))
            .Where(entry => entry.Alias.Length > 0 && entry.CharacterName.Length > 0)
            .GroupBy(entry => entry.Alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var conflictingRequest = normalized.FirstOrDefault(group =>
            group.Select(entry => entry.CharacterName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (conflictingRequest is not null)
            throw new InvalidOperationException($"The request contains different targets for alias '{conflictingRequest.Key}'.");

        await ReconcileAliasesAsync();

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entries = normalized.ToDictionary(group => group.Key, group => group.First().CharacterName,
            StringComparer.OrdinalIgnoreCase);
        var existing = await context.Aliases
            .Where(alias => alias.Game == gameName && entries.Keys.Contains(alias.Alias))
            .ToDictionaryAsync(alias => alias.Alias, StringComparer.OrdinalIgnoreCase);

        foreach (var (alias, characterName) in entries)
        {
            if (existing.TryGetValue(alias, out var existingAlias))
            {
                existingAlias.CharacterName = characterName;
            }
            else
            {
                context.Aliases.Add(new AliasModel
                {
                    Game = gameName,
                    Alias = alias,
                    CharacterName = characterName
                });
            }
        }

        await context.SaveChangesAsync();
        await RefreshCacheFromDatabaseAsync(gameName);

        m_Logger.LogInformation("Upserted {Count} aliases for {Game}", entries.Count, gameName);
    }

    public async Task DeleteAlias(Game gameName, string alias)
    {
        var normalized = AliasModel.NormalizeAlias(alias);
        if (normalized.Length == 0)
            return;

        await ReconcileAliasesAsync();

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var entity = await context.Aliases
            .FirstOrDefaultAsync(entry => entry.Game == gameName && entry.Alias == normalized);

        if (entity == null)
        {
            m_Logger.LogInformation("Alias {Alias} not found for game {Game}; nothing to delete", normalized, gameName);
            return;
        }

        context.Aliases.Remove(entity);
        await context.SaveChangesAsync();
        await RefreshCacheFromDatabaseAsync(gameName);

        m_Logger.LogInformation("Deleted alias {Alias} for game {Game}", normalized, gameName);
    }

    public async Task UpdateAllAliasesAsync()
    {
        await ReconcileAliasesAsync();

        var games = Enum.GetValues<Game>();
        await Task.WhenAll(games.Select(RefreshCacheFromDatabaseAsync));
    }

    private async Task RefreshCacheFromDatabaseAsync(Game gameName)
    {
        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        var aliases = await context.Aliases
            .AsNoTracking()
            .Where(alias => alias.Game == gameName)
            .OrderBy(alias => alias.Alias)
            .ToListAsync();

        var transaction = m_Redis.GetDatabase().CreateTransaction();
        _ = transaction.KeyDeleteAsync(GetAliasKey(gameName));
        if (aliases.Count > 0)
        {
            _ = transaction.HashSetAsync(GetAliasKey(gameName), aliases
                .Select(alias => new HashEntry(alias.Alias, alias.CharacterName))
                .ToArray());
        }

        if (!await transaction.ExecuteAsync())
            throw new RedisException("Redis transaction was not committed.");
    }
}
