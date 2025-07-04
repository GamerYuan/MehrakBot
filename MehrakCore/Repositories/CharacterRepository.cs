﻿#region

using MehrakCore.Models;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace MehrakCore.Repositories;

public class CharacterRepository : ICharacterRepository
{
    private readonly ILogger<CharacterRepository> m_Logger;
    private readonly IMongoCollection<CharacterModel> m_Characters;

    public CharacterRepository(MongoDbService mongoDbService, ILogger<CharacterRepository> logger)
    {
        m_Logger = logger;
        m_Characters = mongoDbService.Characters;
    }

    public async Task<List<string>> GetCharactersAsync(GameName gameName)
    {
        m_Logger.LogInformation("Retrieving characters for game {GameName} from database", gameName);
        var characterModel = await GetCharacterModelAsync(gameName);
        return characterModel?.Characters ?? new List<string>();
    }

    public async Task<CharacterModel?> GetCharacterModelAsync(GameName gameName)
    {
        m_Logger.LogDebug("Retrieving character model for game {GameName} from database", gameName);
        var filter = Builders<CharacterModel>.Filter.Eq(c => c.Game, gameName);
        return await m_Characters.Find(filter).FirstOrDefaultAsync();
    }

    public async Task UpsertCharactersAsync(CharacterModel characterModel)
    {
        m_Logger.LogInformation("Upserting characters for game {GameName} with {Count} characters",
            characterModel.Game, characterModel.Characters.Count);

        // Check if a document exists for this game
        var existing = await m_Characters.Find(c => c.Game == characterModel.Game).FirstOrDefaultAsync();

        if (existing != null)
        {
            // Update existing document - preserve the existing Id
            characterModel.Id = existing.Id;
            await m_Characters.ReplaceOneAsync(
                c => c.Game == characterModel.Game,
                characterModel);
        }
        else
        {
            // Insert new document - let MongoDB generate the Id
            characterModel.Id = null;
            await m_Characters.InsertOneAsync(characterModel);
        }

        m_Logger.LogInformation("Successfully upserted characters for game {GameName}", characterModel.Game);
    }
}
