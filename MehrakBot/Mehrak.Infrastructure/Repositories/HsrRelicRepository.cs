#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Repositories;

internal class HsrRelicRepository : IRelicRepository
{
    private readonly RelicDbContext m_Context;
    private readonly ILogger<HsrRelicRepository> m_Logger;

    public HsrRelicRepository(RelicDbContext context,
        ILogger<HsrRelicRepository> logger)
    {
        m_Context = context;
        m_Logger = logger;
    }

    public async Task AddSetName(int setId, string setName)
    {
        // Upsert: if exists, do nothing; if not, insert
        var existing = await m_Context.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
        if (existing == null)
        {
            var entity = new HsrRelicModel { SetId = setId, SetName = setName };
            m_Context.HsrRelics.Add(entity);
            await m_Context.SaveChangesAsync();
            m_Logger.LogInformation("Inserted relic set mapping: setId {SetId} -> {SetName}", setId, setName);
        }
        else
        {
            // Do not overwrite existing name as per original behavior
            m_Logger.LogInformation("Relic set mapping for setId {SetId} already exists; skipping overwrite", setId);
        }
    }

    public async Task<string> GetSetName(int setId)
    {
        var doc = await m_Context.HsrRelics.AsNoTracking().FirstOrDefaultAsync(x => x.SetId == setId);
        if (doc == null)
        {
            m_Logger.LogWarning("Set name for setId {SetId} not found", setId);
            return string.Empty;
        }

        m_Logger.LogInformation("Retrieved set name for setId {SetId}: {SetName}", setId, doc.SetName);
        return doc.SetName;
    }
}
