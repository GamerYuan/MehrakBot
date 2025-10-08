using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Repositories;

public class AliasRepository : IAliasRepository
{
    private readonly IMongoCollection<AliasModel> m_AliasesCollection;
    private readonly ILogger<AliasRepository> m_Logger;

    public AliasRepository(MongoDbService mongoDbService, ILogger<AliasRepository> logger)
    {
        m_Logger = logger;
        m_AliasesCollection = mongoDbService.Aliases;
    }

    public async Task<Dictionary<string, string>> GetAliasesAsync(GameName gameName)
    {
        m_Logger.LogInformation("Fetching aliases for game: {GameName}", gameName);
        return await m_AliasesCollection
            .Find(alias => alias.Game == gameName)
            .Project(alias => alias.Alias)
            .FirstOrDefaultAsync() ?? new Dictionary<string, string>();
    }

    public async Task UpsertCharacterAliasesAsync(AliasModel aliasModel)
    {
        m_Logger.LogInformation("Upserting aliases for game {GameName} with {Count} aliases", aliasModel.Game,
            aliasModel.Alias.Count);

        var existing = await m_AliasesCollection.Find(x => x.Game == aliasModel.Game).FirstOrDefaultAsync();
        if (existing != null)
        {
            m_Logger.LogInformation("Updating existing aliases for game {GameName}", aliasModel.Game);
            aliasModel.Id = existing.Id;
            await m_AliasesCollection.ReplaceOneAsync(x => x.Id == existing.Id, aliasModel);
        }
        else
        {
            m_Logger.LogInformation("Inserting new aliases for game {GameName}", aliasModel.Game);
            await m_AliasesCollection.InsertOneAsync(aliasModel);
        }
    }
}
