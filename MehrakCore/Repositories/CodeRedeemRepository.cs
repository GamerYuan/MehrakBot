#region

using MehrakCore.Models;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace MehrakCore.Repositories;

public class CodeRedeemRepository : ICodeRedeemRepository
{
    private readonly ILogger<CodeRedeemRepository> m_Logger;
    private readonly IMongoCollection<CodeRedeemModel> m_Collection;

    public CodeRedeemRepository(MongoDbService service, ILogger<CodeRedeemRepository> logger)
    {
        m_Logger = logger;
        m_Collection = service.Codes;
    }

    public async Task<List<string>> GetCodesAsync(GameName gameName)
    {
        m_Logger.LogDebug("Fetching codes for game: {GameName}", gameName);
        return (await m_Collection
            .Find(x => x.Game == gameName).FirstOrDefaultAsync()).Codes ?? [];
    }

    public async Task AddCodesAsync(GameName gameName, List<string> codes)
    {
        var entry = await m_Collection
            .Find(x => x.Game == gameName).FirstOrDefaultAsync();
        if (entry == null)
        {
            entry = new CodeRedeemModel
            {
                Game = gameName,
                Codes = []
            };
            await m_Collection.InsertOneAsync(entry);
        }

        var newCodes = codes
            .Where(c => !entry.Codes.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (newCodes.Count == 0)
        {
            m_Logger.LogDebug("No new codes to add for game: {GameName}", gameName);
            return;
        }

        entry.Codes.AddRange(newCodes);
        m_Logger.LogInformation("Adding {Count} new codes for game: {GameName}", newCodes.Count, gameName);
        await m_Collection.ReplaceOneAsync(
            x => x.Game == gameName, entry, new ReplaceOptions { IsUpsert = true });
    }
}
