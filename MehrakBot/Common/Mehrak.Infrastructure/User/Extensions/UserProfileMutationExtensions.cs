using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mehrak.Infrastructure.User.Extensions;

/// <summary>
/// Shared transaction boundary for mutations that allocate or compact a user's
/// profile IDs.
/// </summary>
public static class UserProfileMutationExtensions
{
    /// <summary>
    /// Runs a user-profile mutation while holding a transaction-scoped lock
    /// shared by every process connected to the PostgreSQL database.
    /// </summary>
    public static async Task<TResult> ExecuteUserProfileMutationAsync<TResult>(
        this UserDbContext context,
        long userId,
        Func<Task<TResult>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mutation);

        IDbContextTransaction? transaction = null;
        var ownsTransaction = false;

        if (context.Database.IsRelational() && context.Database.CurrentTransaction is null)
        {
            transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            ownsTransaction = true;
        }

        try
        {
            if (IsPostgreSql(context))
            {
                var lockKey = $"mehrak:user-profile:{userId}";
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    cancellationToken);
            }

            var result = await mutation();
            if (ownsTransaction)
                await transaction!.CommitAsync(cancellationToken);

            return result;
        }
        finally
        {
            if (ownsTransaction)
                await transaction!.DisposeAsync();
        }
    }

    private static bool IsPostgreSql(UserDbContext context) =>
        string.Equals(
            context.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);
}
