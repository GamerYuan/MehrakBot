#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class HsrRelicRepository : IRelicRepository
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<HsrRelicRepository> m_Logger;

    public HsrRelicRepository(IServiceScopeFactory scopeFactory,
        ILogger<HsrRelicRepository> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task AddSetName(int setId, string setName)
    {
        // Upsert: if exists, do nothing; if not, insert
        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RelicDbContext>();

        var existing = await context.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
        if (existing == null)
        {
            var entity = new HsrRelicModel { SetId = setId, SetName = setName };
            context.HsrRelics.Add(entity);
            await context.SaveChangesAsync();
            m_Logger.LogDebug("Inserted relic set mapping: setId {SetId} -> {SetName}", setId, setName);
        }
        else
        {
            // Do not overwrite existing name as per original behavior
            m_Logger.LogDebug("Relic set mapping for setId {SetId} already exists; skipping overwrite", setId);
        }
    }

    public async Task<string> GetSetName(int setId)
    {
        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RelicDbContext>();

        var doc = await context.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
        if (doc == null)
        {
            m_Logger.LogWarning("Set name for setId {SetId} not found", setId);
            return string.Empty;
        }

        m_Logger.LogDebug("Retrieved set name for setId {SetId}: {SetName}", setId, doc.SetName);
        return doc.SetName;
    }
}
