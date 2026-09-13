using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Character.Services;

internal static class CharacterDbLock
{
    public static Task AcquireAsync(DbContext context, string resource, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return Task.CompletedTask;

        return context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
    }

}
