using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Repositories;

internal class UserRepository : IUserRepository
{
    private readonly UserContext m_Context;
    private readonly ILogger<UserRepository> m_Logger;

    public UserRepository(UserContext context, ILogger<UserRepository> logger)
    {
        m_Context = context ?? throw new ArgumentNullException(nameof(context));
        m_Logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(long userId)
    {
        m_Logger.LogDebug("Retrieving user {UserId} from database", userId);

        var user = await m_Context.Users
            .AsNoTracking()
            .Include(u => u.Profiles)
                .ThenInclude(p => p.GameUids)
            .Include(u => u.Profiles)
                .ThenInclude(p => p.LastUsedRegions)
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            m_Logger.LogDebug("User {UserId} retrieval result: {Result}", userId, "Not Found");
            return null;
        }

        var dto = MapToDto(user);
        m_Logger.LogDebug("User {UserId} retrieval result: {Result}", userId, "Found");
        return dto;
    }

    public async Task CreateOrUpdateUserAsync(UserDto user)
    {
        try
        {
            m_Logger.LogInformation("Creating or updating user {UserId} in database", user.Id);

            // Ensure timestamp is set similar to Mongo implementation
            user.Timestamp = DateTime.UtcNow;

            // Load existing with children
            var existing = await m_Context.Users
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.GameUids)
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.LastUsedRegions)
                .SingleOrDefaultAsync(u => u.Id == user.Id);

            if (existing == null)
            {
                var newModel = MapToModel(user);
                await m_Context.Users.AddAsync(newModel);
            }
            else
            {
                // Update root
                existing.Timestamp = user.Timestamp;

                // Replace profiles to keep parity with document upsert behavior
                // Remove existing children (cascade will handle children but we need explicit for update)
                m_Context.UserProfiles.RemoveRange(existing.Profiles);

                existing.Profiles.Clear();

                var rebuiltProfiles = BuildProfileModels(user, existing.Id);
                foreach (var p in rebuiltProfiles)
                {
                    existing.Profiles.Add(p);
                }

                m_Context.Users.Update(existing);
            }

            await m_Context.SaveChangesAsync();
            m_Logger.LogInformation("User {UserId} successfully saved to database", user.Id);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error saving user {UserId} to database", user.Id);
            throw;
        }
    }

    public async Task<bool> DeleteUserAsync(long userId)
    {
        try
        {
            m_Logger.LogInformation("Attempting to delete user {UserId} from database", userId);

            var user = await m_Context.Users.FindAsync(userId);
            if (user == null)
            {
                m_Logger.LogInformation("Delete user {UserId} result: {Result}", userId, "Not Found");
                return false;
            }

            m_Context.Users.Remove(user);
            var affected = await m_Context.SaveChangesAsync();
            var success = affected > 0;

            m_Logger.LogInformation("Delete user {UserId} result: {Result}", userId, success ? "Deleted" : "Not Found");
            return success;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error deleting user {UserId} from database", userId);
            throw;
        }
    }

    private static UserDto MapToDto(UserModel model)
    {
        var dto = new UserDto
        {
            Id = model.Id,
            Timestamp = model.Timestamp,
            Profiles = model.Profiles.Select(p => new UserProfileDto
            {
                ProfileId = p.ProfileId,
                LtUid = p.LtUid,
                LToken = p.LToken,
                LastCheckIn = p.LastCheckIn,
                GameUids = p.GameUids.Count == 0
                    ? null
                    : p.GameUids
                        .GroupBy(g => g.Game)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToDictionary(x => x.Region, x => x.GameUid)
                        ),
                LastUsedRegions = p.LastUsedRegions.Count == 0
                    ? null
                    : p.LastUsedRegions.ToDictionary(r => r.Game, r => r.Region)
            })
            .ToList()
        };
        return dto;
    }

    private static UserModel MapToModel(UserDto dto)
    {
        var model = new UserModel
        {
            Id = dto.Id,
            Timestamp = dto.Timestamp,
            Profiles = BuildProfileModels(dto, dto.Id)
        };
        return model;
    }

    private static List<UserProfileModel> BuildProfileModels(UserDto dto, long userId)
    {
        var profiles = new List<UserProfileModel>();
        if (dto.Profiles == null)
            return profiles;

        foreach (var p in dto.Profiles)
        {
            var profile = new UserProfileModel
            {
                UserId = userId,
                ProfileId = p.ProfileId,
                LtUid = p.LtUid,
                LToken = p.LToken,
                LastCheckIn = p.LastCheckIn,
                GameUids = [],
                LastUsedRegions = []
            };

            if (p.GameUids != null)
            {
                foreach (var gameEntry in p.GameUids)
                {
                    var game = gameEntry.Key;
                    foreach (var regionEntry in gameEntry.Value)
                    {
                        profile.GameUids.Add(new ProfileGameUid
                        {
                            Game = game,
                            Region = regionEntry.Key,
                            GameUid = regionEntry.Value
                        });
                    }
                }
            }

            if (p.LastUsedRegions != null)
            {
                foreach (var region in p.LastUsedRegions)
                {
                    profile.LastUsedRegions.Add(new ProfileRegion
                    {
                        Game = region.Key,
                        Region = region.Value
                    });
                }
            }

            profiles.Add(profile);
        }

        return profiles;
    }
}
