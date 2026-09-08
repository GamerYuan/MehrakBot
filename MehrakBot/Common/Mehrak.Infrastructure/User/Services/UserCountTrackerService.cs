using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mehrak.Infrastructure.User.Services;

/// <summary>
/// Reads the number of users with at least one profile from PostgreSQL.
/// The database is authoritative; a Redis counter cannot safely reconcile mutations from both Bot and Dashboard.
/// </summary>
public class UserCountTrackerService
{
    private readonly IServiceScopeFactory m_ScopeFactory;

    public UserCountTrackerService(IServiceScopeFactory scopeFactory)
    {
        m_ScopeFactory = scopeFactory;
    }

    public async Task<int> GetUserCountAsync(CancellationToken cancellationToken = default)
    {
        using var scope = m_ScopeFactory.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();
        return await userContext.Users.CountAsync(user => user.Profiles.Any(), cancellationToken);
    }
}
