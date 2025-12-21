using Mehrak.Domain.Services;
using Mehrak.Infrastructure.Context;

namespace Mehrak.Infrastructure.Services;

internal class DbStatusService : IDbStatusService
{
    private readonly UserDbContext m_UserDbContext;

    public DbStatusService(UserDbContext userDbContext)
    {
        m_UserDbContext = userDbContext;
    }

    public async Task<bool> GetDbStatus()
    {
        return await m_UserDbContext.Database.CanConnectAsync();
    }
}
