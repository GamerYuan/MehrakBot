#region

using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Character.Services;

public class AliasService : IAliasService
{
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<AliasService> m_Logger;

    public AliasService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AliasService> logger)
    {
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
    }

    public Dictionary<string, string> GetAliases(Game gameName)
    {
        // Preserve display spelling while keeping alias lookup case-insensitive.
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

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await CharacterDbLock.AcquireAsync(context, $"aliases:{gameName}");

        var entries = normalized.ToDictionary(group => group.Key, group => group.First().CharacterName,
            StringComparer.OrdinalIgnoreCase);
        // Match with the same comparer used for request deduplication and lookup,
        // rather than relying on the database's case-sensitive alias comparison.
        var storedAliases = await context.Aliases
            .Where(alias => alias.Game == gameName)
            .ToListAsync();
        var existing = storedAliases
            .Where(alias => entries.ContainsKey(alias.Alias))
            .ToDictionary(alias => alias.Alias, StringComparer.OrdinalIgnoreCase);

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
        await transaction.CommitAsync();

        m_Logger.LogInformation("Upserted {Count} aliases for {Game}", entries.Count, gameName);
    }

    public async Task DeleteAlias(Game gameName, string alias)
    {
        var normalized = AliasModel.NormalizeAlias(alias);
        if (normalized.Length == 0)
            return;

        using var scope = m_ServiceScopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await CharacterDbLock.AcquireAsync(context, $"aliases:{gameName}");

        var storedAliases = await context.Aliases
            .Where(entry => entry.Game == gameName)
            .ToListAsync();
        var entity = storedAliases.FirstOrDefault(entry =>
            string.Equals(entry.Alias, normalized, StringComparison.OrdinalIgnoreCase));

        if (entity == null)
        {
            m_Logger.LogInformation("Alias {Alias} not found for game {Game}; nothing to delete", normalized, gameName);
            await transaction.CommitAsync();
            return;
        }

        context.Aliases.Remove(entity);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        m_Logger.LogInformation("Deleted alias {Alias} for game {Game}", normalized, gameName);
    }
}
