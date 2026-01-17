#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class CodeRedeemRepository : ICodeRedeemRepository
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<CodeRedeemRepository> m_Logger;

    public CodeRedeemRepository(IServiceScopeFactory scopeFactory, ILogger<CodeRedeemRepository> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task<List<string>> GetCodesAsync(Game gameName)
    {
        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodeRedeemDbContext>();

        m_Logger.LogDebug("Fetching codes for game: {Game}", gameName);
        return await context.Codes.AsNoTracking()
            .Where(x => x.Game == gameName)
            .Select(x => x.Code)
            .ToListAsync();
    }

    public async Task UpdateCodesAsync(Game gameName, Dictionary<string, CodeStatus> codes)
    {
        var incoming = codes.Select(x => x.Key).ToHashSet();

        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CodeRedeemDbContext>();

        var existingCodes = await context.Codes.Where(x => x.Game == gameName && incoming.Contains(x.Code)).ToListAsync();

        var expiredCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Invalid)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<CodeRedeemModel> codesToRemove = [];

        if (expiredCodes.Count > 0)
        {
            codesToRemove.AddRange(existingCodes.Where(x => expiredCodes.Contains(x.Code)));
            context.Codes.RemoveRange(codesToRemove);
        }

        var newValidCodes = codes
            .Where(kvp => kvp.Value == CodeStatus.Valid)
            .Select(kvp => kvp.Key)
            .Except(existingCodes.Select(x => x.Code), StringComparer.OrdinalIgnoreCase)
            .Select(x => new CodeRedeemModel
            {
                Game = gameName,
                Code = x
            })
            .ToList();

        if (newValidCodes.Count > 0)
        {
            context.Codes.AddRange(newValidCodes);
        }

        await context.SaveChangesAsync();

        m_Logger.LogDebug("Added {Count} new codes, removed {Removed} expired codes for game: {Game}.",
            newValidCodes.Count, codesToRemove.Count, gameName);
    }
}
