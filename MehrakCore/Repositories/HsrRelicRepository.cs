using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MehrakCore.Repositories;

public interface IRelicRepository<T>
{
    public Task AddSetName(int setId, string setName);

    public Task<string> GetSetName(int setId);
}

public class HsrRelicRepository : IRelicRepository<Relic>
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
        string fieldPath = $"set_names.{setId}";
        FilterDefinition<HsrRelicModel> filter = Builders<HsrRelicModel>.Filter.Exists(fieldPath, false);
        UpdateDefinition<HsrRelicModel> update = Builders<HsrRelicModel>.Update.Set(fieldPath, setName);
        UpdateOptions options = new() { IsUpsert = true };

        UpdateResult result = await m_MongoCollection.UpdateOneAsync(filter, update, options);

        if (result.UpsertedId != null)
        {
            m_Logger.LogInformation("Inserted new relic set mapping and added set name for setId {SetId} to {SetName}", setId, setName);
        }
        else if (result.ModifiedCount > 0)
        {
            m_Logger.LogInformation("Added set name for setId {SetId} to {SetName}", setId, setName);
        }
        else
        {
            m_Logger.LogInformation("Skipped adding set name for setId {SetId} because it already exists", setId);
        }
    }

    public async Task<string> GetSetName(int setId)
    {
        HsrRelicModel doc = await (await m_MongoCollection.FindAsync(_ => true)).FirstOrDefaultAsync();
        if (doc == null || !doc.SetNames.TryGetValue(setId, out string? value))
        {
            m_Logger.LogWarning("Set name for setId {SetId} not found", setId);
            return string.Empty;
        }

        m_Logger.LogInformation("Retrieved set name for setId {SetId}: {SetName}", setId, value);
        return value;
    }
}
