using Mehrak.Application.Repositories;
using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Repositories;

public class HsrRelicRepository : IRelicRepository
{
    private readonly IMongoCollection<HsrRelicModel> m_MongoCollection;
    private readonly ILogger<HsrRelicRepository> m_Logger;

    public HsrRelicRepository(MongoDbService mongoService,
        ILogger<HsrRelicRepository> logger)
    {
        m_MongoCollection = mongoService.HsrRelics;
        m_Logger = logger;
    }

    public async Task AddSetName(int setId, string setName)
    {
        FilterDefinition<HsrRelicModel> filter = Builders<HsrRelicModel>.Filter.Eq(x => x.SetId, setId);
        UpdateDefinition<HsrRelicModel> update = Builders<HsrRelicModel>.Update
            .SetOnInsert(x => x.SetId, setId)
            .SetOnInsert(x => x.SetName, setName);

        UpdateOptions options = new() { IsUpsert = true };
        UpdateResult result = await m_MongoCollection.UpdateOneAsync(filter, update, options);

        if (result.UpsertedId != null)
        {
            m_Logger.LogInformation("Inserted relic set mapping: setId {SetId} -> {SetName}", setId, setName);
        }
        else if (result.ModifiedCount > 0)
        {
            m_Logger.LogInformation("Added relic set mapping for setId {SetId}", setId);
        }
        else
        {
            m_Logger.LogInformation("Relic set mapping for setId {SetId} already exists; skipping overwrite", setId);
        }
    }

    public async Task<string> GetSetName(int setId)
    {
        FilterDefinition<HsrRelicModel> filter = Builders<HsrRelicModel>.Filter.Eq(x => x.SetId, setId);
        HsrRelicModel? doc = await m_MongoCollection.Find(filter).FirstOrDefaultAsync();
        if (doc == null)
        {
            m_Logger.LogWarning("Set name for setId {SetId} not found", setId);
            return string.Empty;
        }

        m_Logger.LogInformation("Retrieved set name for setId {SetId}: {SetName}", setId, doc.SetName);
        return doc.SetName;
    }
}
