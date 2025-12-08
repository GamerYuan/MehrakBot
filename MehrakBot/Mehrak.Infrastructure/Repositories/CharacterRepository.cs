#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class CharacterRepository : ICharacterRepository
{
    private readonly CharacterDbContext m_Context;
    private readonly ILogger<CharacterRepository> m_Logger;

    public CharacterRepository(CharacterDbContext context, ILogger<CharacterRepository> logger)
    {
        m_Context = context;
        m_Logger = logger;
    }

    public async Task<List<string>> GetCharactersAsync(Game gameName)
    {
        m_Logger.LogInformation("Retrieving characters for game {Game} from database", gameName);

        return await m_Context.Characters.Where(x => x.Game == gameName)
            .Select(x => x.Name)
            .ToListAsync();
    }

    public async Task UpsertCharactersAsync(Game gameName, IEnumerable<string> characters)
    {
        var incoming = characters.ToHashSet();

        if (incoming.Count == 0) return;

        var existing = await m_Context.Characters
            .Where(x => x.Game == gameName && incoming.Contains(x.Name))
            .Select(x => x.Name)
            .ToListAsync();

        var newEntities = incoming.Except(existing).Select(name => new CharacterModel
        {
            Game = gameName,
            Name = name
        }).ToList();

        if (!newEntities.Any()) return;

        m_Logger.LogInformation("Upserting characters for game {Game} with {Count} characters",
            gameName, newEntities.Count);

        m_Context.Characters.AddRange(newEntities);
        await m_Context.SaveChangesAsync();
    }
}
