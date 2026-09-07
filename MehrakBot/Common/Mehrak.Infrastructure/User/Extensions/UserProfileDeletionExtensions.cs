using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mehrak.Infrastructure.User.Extensions;

/// <summary>
/// Database mutations for deleting and reindexing user profiles.
/// </summary>
public static class UserProfileDeletionExtensions
{
    /// <summary>
    /// Deletes one profile and closes the user-facing profile ID gap. A
    /// temporary negative ID phase avoids conflicts with the unique
    /// (UserId, ProfileId) index on PostgreSQL. Only ProfileId is changed on
    /// surviving rows; credentials, check-in state and other scalars are not
    /// written from the deletion snapshot.
    /// </summary>
    public static async Task DeleteAndReindexProfilesAsync(
        this UserDbContext context,
        UserProfileModel profileToDelete,
        IReadOnlyCollection<UserProfileModel> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profileToDelete);
        ArgumentNullException.ThrowIfNull(profiles);

        var reindexedProfiles = profiles
            .Where(profile => profile.Id != profileToDelete.Id && profile.ProfileId > profileToDelete.ProfileId)
            .OrderBy(profile => profile.ProfileId)
            .Select((profile, index) => new ReindexEntry(
                profile,
                profile.ProfileId - 1,
                int.MinValue + index))
            .ToArray();

        context.UserProfiles.Remove(profileToDelete);
        foreach (var entry in reindexedProfiles)
            SetProfileIdOnly(context, entry.Profile, entry.TemporaryProfileId);

        IDbContextTransaction? transaction = null;
        if (context.Database.IsRelational())
            transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // The delete and temporary IDs are conflict-free regardless of
            // the order chosen by the provider for the individual commands.
            await context.SaveChangesAsync(cancellationToken);

            foreach (var entry in reindexedProfiles)
                SetProfileIdOnly(context, entry.Profile, entry.FinalProfileId);

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static void SetProfileIdOnly(UserDbContext context, UserProfileModel profile, int profileId)
    {
        var entry = context.Entry(profile);
        entry.State = EntityState.Unchanged;
        profile.ProfileId = profileId;
        entry.Property(current => current.ProfileId).IsModified = true;
    }

    private sealed record ReindexEntry(
        UserProfileModel Profile,
        int FinalProfileId,
        int TemporaryProfileId);
}
