#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class CharacterRepository : ICharacterRepository
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<CharacterRepository> m_Logger;

    public CharacterRepository(IServiceScopeFactory scopeFactory, ILogger<CharacterRepository> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task<List<string>> GetCharactersAsync(Game gameName)
    {
        m_Logger.LogDebug("Retrieving characters for game {Game} from database", gameName);

        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        return await context.Characters.Where(x => x.Game == gameName)
            .Select(x => x.Name)
            .ToListAsync();
    }

    public async Task UpsertCharactersAsync(Game gameName, IEnumerable<string> characters)
    {
        var incoming = characters.ToHashSet();

        if (incoming.Count == 0) return;

        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();

        var existing = await context.Characters
            .Where(x => x.Game == gameName && incoming.Contains(x.Name))
            .Select(x => x.Name)
            .ToListAsync();

        var newEntities = incoming.Except(existing).Select(name => new CharacterModel
        {
            Game = gameName,
            Name = name
        }).ToList();

        if (!newEntities.Any()) return;

        m_Logger.LogDebug("Upserting characters for game {Game} with {Count} characters",
            gameName, newEntities.Count);

        context.Characters.AddRange(newEntities);
        await context.SaveChangesAsync();
    }
}
