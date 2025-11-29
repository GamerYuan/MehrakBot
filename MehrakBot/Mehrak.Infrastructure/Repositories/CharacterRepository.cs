#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class CharacterRepository : ICharacterRepository
{
    private readonly ILogger<CharacterRepository> m_Logger;
    private readonly IMongoCollection<CharacterModel> m_Characters;

    public CharacterRepository(MongoDbService mongoDbService, ILogger<CharacterRepository> logger)
    {
        m_Logger = logger;
        m_Characters = mongoDbService.Characters;
    }

    public async Task<List<string>> GetCharactersAsync(Game gameName)
    {
        m_Logger.LogInformation("Retrieving characters for game {Game} from database", gameName);
        CharacterModel? characterModel = await GetCharacterModelAsync(gameName);
        return characterModel?.Characters ?? [];
    }

    public async Task<CharacterModel?> GetCharacterModelAsync(Game gameName)
    {
        m_Logger.LogDebug("Retrieving character model for game {Game} from database", gameName);
        FilterDefinition<CharacterModel> filter = Builders<CharacterModel>.Filter.Eq(c => c.Game, gameName);
        return await m_Characters.Find(filter).FirstOrDefaultAsync();
    }

    public async Task UpsertCharactersAsync(Game gameName, IEnumerable<string> characters)
    {
        var charList = characters.ToList();

        m_Logger.LogInformation("Upserting characters for game {Game} with {Count} characters",
            gameName, charList);

        FilterDefinition<CharacterModel> filter = Builders<CharacterModel>.Filter.Eq(c => c.Game, gameName);
        UpdateDefinition<CharacterModel> update = Builders<CharacterModel>.Update
            .AddToSetEach(x => x.Characters, charList);

        await m_Characters.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }
}
