#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class AliasRepository : IAliasRepository
{
    private readonly CharacterDbContext m_Context;
    private readonly ILogger<AliasRepository> m_Logger;

    public AliasRepository(CharacterDbContext context, ILogger<AliasRepository> logger)
    {
        m_Context = context;
        m_Logger = logger;
    }

    public async Task<Dictionary<string, string>> GetAliasesAsync(Game gameName)
    {
        m_Logger.LogDebug("Fetching aliases for game: {Game}", gameName);
        return await m_Context.Aliases.AsNoTracking()
            .Where(x => x.Game == gameName)
            .ToDictionaryAsync(x => x.Alias, x => x.CharacterName);
    }

    public async Task UpsertAliasAsync(Game gameName, Dictionary<string, string> alias)
    {
        if (alias.Count == 0)
            return;

        // Load existing aliases for the provided keys
        var aliasKeys = alias.Keys.ToList();
        var existing = await m_Context.Aliases
            .Where(x => x.Game == gameName && aliasKeys.Contains(x.Alias))
            .ToListAsync();

        // Map for quick lookup
        var existingMap = existing.ToDictionary(x => x.Alias, x => x);

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
                    m_Context.Aliases.Update(model);
                }
            }
            else
            {
                // Insert new row
                m_Context.Aliases.Add(new AliasModel
                {
                    Game = gameName,
                    Alias = key,
                    CharacterName = character
                });
            }
        }

        await m_Context.SaveChangesAsync();
    }
}
