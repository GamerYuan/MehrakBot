#region

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
        var filter = Builders<CharacterModel>.Filter.Eq(c => c.Game, gameName);
        return (await m_Characters.Find(filter).FirstOrDefaultAsync()).Characters;
    }
}
