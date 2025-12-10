#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class AliasRepository : IAliasRepository
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<AliasRepository> m_Logger;

    public AliasRepository(IServiceScopeFactory scopeFactory, ILogger<AliasRepository> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task<Dictionary<string, string>> GetAliasesAsync(Game gameName)
    {
        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        m_Logger.LogDebug("Fetching aliases for game: {Game}", gameName);
        return await context.Aliases.AsNoTracking()
            .Where(x => x.Game == gameName)
            .ToDictionaryAsync(x => x.Alias, x => x.CharacterName);
    }

    public async Task UpsertAliasAsync(Game gameName, Dictionary<string, string> alias)
    {
        if (alias.Count == 0)
            return;

        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        // Load existing aliases for the provided keys
        var aliasKeys = alias.Keys.ToList();
        var existing = await context.Aliases
            .Where(x => x.Game == gameName && aliasKeys.Contains(x.Alias))
            .ToListAsync();

        // Map for quick lookup
        var existingMap = existing.ToDictionary(x => x.Alias, x => x);

        int updateCount = 0, newCount = 0;

        foreach (var kvp in alias)
        {
            var key = kvp.Key;
            var character = kvp.Value;

            if (existingMap.TryGetValue(key, out var model))
            {
                // Update existing row's character
                if (!string.Equals(model.CharacterName, character, StringComparison.OrdinalIgnoreCase))
                {
                    model.CharacterName = character;
                    context.Aliases.Update(model);
                    updateCount++;
                }
            }
            else
            {
                // Insert new row
                context.Aliases.Add(new AliasModel
                {
                    Game = gameName,
                    Alias = key,
                    CharacterName = character
                });
                newCount++;
            }
        }

        m_Logger.LogDebug("Upsert completed. Updated {UpdateCount} entries, Created {NewCount} entries",
            updateCount, newCount);

        await context.SaveChangesAsync();
    }
}
