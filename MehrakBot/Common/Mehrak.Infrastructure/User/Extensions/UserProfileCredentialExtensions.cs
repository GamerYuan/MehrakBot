using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.User.Extensions;

/// <summary>
/// Database mutations for profile credentials that must not overwrite a
/// newer revision while a login is in flight.
/// </summary>
public static class UserProfileCredentialExtensions
{
    /// <summary>
    /// Replaces a legacy LToken only when the row still contains the exact
    /// ciphertext that was decrypted. Relational providers use a conditional
    /// UPDATE so the compare and swap is performed by the database.
    /// </summary>
    public static async Task<bool> TryCompareAndSwapLTokenAsync(
        this UserDbContext context,
        long profileRowId,
        string expectedLToken,
        string replacementLToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(expectedLToken);
        ArgumentNullException.ThrowIfNull(replacementLToken);

        if (context.Database.IsRelational())
        {
            var affectedRows = await context.UserProfiles
                .Where(profile => profile.Id == profileRowId && profile.LToken == expectedLToken)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(profile => profile.LToken, replacementLToken),
                    cancellationToken);

            if (affectedRows != 1)
                return false;

            // ExecuteUpdateAsync intentionally bypasses the change tracker.
            // Keep an already-tracked entity coherent for callers that query
            // the current revision again on this same context.
            var trackedEntry = context.ChangeTracker.Entries<UserProfileModel>()
                .FirstOrDefault(entry => entry.Entity.Id == profileRowId);
            if (trackedEntry is not null)
            {
                var tokenProperty = trackedEntry.Property(profile => profile.LToken);
                tokenProperty.CurrentValue = replacementLToken;
                tokenProperty.OriginalValue = replacementLToken;
                tokenProperty.IsModified = false;
            }

            return true;
        }

        // The application uses PostgreSQL. This fallback keeps non-relational
        // test providers usable, while their stores do not provide the
        // database-level compare-and-swap guarantees of the branch above.
        var profile = await context.UserProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.Id == profileRowId, cancellationToken);
        if (profile is null || !string.Equals(profile.LToken, expectedLToken, StringComparison.Ordinal))
            return false;

        var tracked = context.ChangeTracker.Entries<UserProfileModel>()
            .FirstOrDefault(entry => entry.Entity.Id == profileRowId);
        if (tracked is not null)
        {
            tracked.State = EntityState.Unchanged;
            tracked.Entity.LToken = replacementLToken;
            tracked.Property(entry => entry.LToken).IsModified = true;
        }
        else
        {
            context.UserProfiles.Attach(profile);
            profile.LToken = replacementLToken;
            context.Entry(profile).Property(entry => entry.LToken).IsModified = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
