using Mehrak.Application.Repositories;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Repositories;

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
        var entry = await m_Collection
            .Find(x => x.Game == gameName).FirstOrDefaultAsync();
        return entry?.Codes ?? [];
    }

    public async Task AddCodesAsync(GameName gameName, Dictionary<string, CodeStatus> codes)
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

        entry.Codes ??= new List<string>();

        var expiredCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Invalid)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = entry.Codes.RemoveAll(code => expiredCodes.Contains(code));

        var existingCodes = new HashSet<string>(entry.Codes, StringComparer.OrdinalIgnoreCase);
        var newValidCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Valid && !existingCodes.Contains(kvp.Key))
            .Select(kvp => kvp.Key)
            .ToList();

        entry.Codes.AddRange(newValidCodes);

        if (removed == 0 && newValidCodes.Count == 0)
        {
            m_Logger.LogDebug("No changes to codes for game: {GameName}", gameName);
            return;
        }

        m_Logger.LogInformation("Added {Count} new codes, removed {Removed} expired codes for game: {GameName}.",
            newValidCodes.Count, removed, gameName);

        await m_Collection.ReplaceOneAsync(
            x => x.Game == gameName, entry, new ReplaceOptions { IsUpsert = true });
    }
}
