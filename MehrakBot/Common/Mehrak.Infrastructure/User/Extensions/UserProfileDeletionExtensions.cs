using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.User.Extensions;

/// <summary>
/// Database mutations for deleting and reindexing user profiles.
/// </summary>
public static class UserProfileDeletionExtensions
{
    /// <summary>
    /// Deletes the supplied profile row and closes the user-facing profile ID
    /// gap. The stable row ID is retained from the caller's initial lookup,
    /// while all reindexing decisions are made from a fresh snapshot after the
    /// per-user mutation lock has been acquired.
    /// </summary>
    public static async Task<UserProfileDeletionResult?> DeleteAndReindexProfilesAsync(
        this UserDbContext context,
        UserProfileModel profileToDelete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profileToDelete);

        var profileRowId = profileToDelete.Id;
        var userId = profileToDelete.UserId;

        return await context.ExecuteUserProfileMutationAsync(
            userId,
            async () =>
            {
                // The caller may have loaded a stale tracked snapshot before
                // entering the lock. Do not let it affect the fresh plan.
                context.ChangeTracker.Clear();

                var profiles = await context.UserProfiles
                    .Where(profile => profile.UserId == userId)
                    .OrderBy(profile => profile.ProfileId)
                    .ToListAsync(cancellationToken);
                var currentProfile = profiles.FirstOrDefault(profile => profile.Id == profileRowId);
                if (currentProfile is null)
                    return null;

                var reindexedProfiles = profiles
                    .Where(profile => profile.Id != currentProfile.Id && profile.ProfileId > currentProfile.ProfileId)
                    .Select((profile, index) => new ReindexEntry(
                        profile,
                        profile.ProfileId - 1,
                        int.MinValue + index))
                    .ToArray();

                context.UserProfiles.Remove(currentProfile);
                foreach (var entry in reindexedProfiles)
                    SetProfileIdOnly(context, entry.Profile, entry.TemporaryProfileId);

                // The delete and temporary IDs are conflict-free regardless of
                // the order chosen by the provider for the individual commands.
                await context.SaveChangesAsync(cancellationToken);

                foreach (var entry in reindexedProfiles)
                    SetProfileIdOnly(context, entry.Profile, entry.FinalProfileId);

                await context.SaveChangesAsync(cancellationToken);

                return new UserProfileDeletionResult(
                    currentProfile.LtUid,
                    currentProfile.ProfileId,
                    profiles.Count - 1);
            },
            cancellationToken);
    }

    /// <summary>
    /// Compatibility overload for callers that already loaded a profile
    /// collection. The collection is intentionally ignored because it may be
    /// stale by the time the database lock is acquired.
    /// </summary>
    public static async Task DeleteAndReindexProfilesAsync(
        this UserDbContext context,
        UserProfileModel profileToDelete,
        IReadOnlyCollection<UserProfileModel> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        await context.DeleteAndReindexProfilesAsync(profileToDelete, cancellationToken);
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

/// <summary>
/// Details of a profile deletion committed under the per-user mutation lock.
/// </summary>
public sealed record UserProfileDeletionResult(
    long LtUid,
    int ProfileId,
    int RemainingProfileCount);
