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

internal class CodeRedeemRepository : ICodeRedeemRepository
{
    private readonly CodeRedeemDbContext m_Context;
    private readonly ILogger<CodeRedeemRepository> m_Logger;

    public CodeRedeemRepository(CodeRedeemDbContext context, ILogger<CodeRedeemRepository> logger)
    {
        m_Context = context;
        m_Logger = logger;
    }

    public async Task<List<string>> GetCodesAsync(Game gameName)
    {
        m_Logger.LogDebug("Fetching codes for game: {Game}", gameName);
        return await m_Context.Codes.AsNoTracking()
            .Where(x => x.Game == gameName)
            .Select(x => x.Code)
            .ToListAsync();
    }

    public async Task UpdateCodesAsync(Game gameName, Dictionary<string, CodeStatus> codes)
    {
        var existingCodes = await m_Context.Codes.Where(x => x.Game == gameName && codes.ContainsKey(x.Code)).ToListAsync();

        var expiredCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Invalid)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<CodeRedeemModel> codesToRemove = [];

        if (expiredCodes.Count > 0)
        {
            codesToRemove.AddRange(existingCodes.Where(x => expiredCodes.Contains(x.Code)));
            m_Context.Codes.RemoveRange(codesToRemove);
        }

        var newValidCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Valid)
            .Select(kvp => kvp.Key)
            .Except(existingCodes.Select(x => x.Code))
            .Distinct()
            .Select(x => new CodeRedeemModel
            {
                Game = gameName,
                Code = x
            })
            .ToList();

        if (newValidCodes.Count > 0)
        {
            m_Context.Codes.AddRange(newValidCodes);
        }

        await m_Context.SaveChangesAsync();

        m_Logger.LogDebug("Added {Count} new codes, removed {Removed} expired codes for game: {Game}.",
            newValidCodes.Count, codesToRemove.Count, gameName);
    }
}
