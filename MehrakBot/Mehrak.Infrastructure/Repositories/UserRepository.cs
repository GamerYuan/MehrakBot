using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mehrak.Infrastructure.Repositories;

internal class UserRepository : IUserRepository
{
    private readonly IServiceScopeFactory m_ScopeFactory;
    private readonly ILogger<UserRepository> m_Logger;

    public UserRepository(IServiceScopeFactory scopeFactory, ILogger<UserRepository> logger)
    {
        m_ScopeFactory = scopeFactory;
        m_Logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(ulong userId)
    {
        m_Logger.LogDebug("Retrieving user {UserId} from database", userId);

        using var scope = m_ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        var user = await context.Users
            .AsNoTracking()
            .Include(u => u.Profiles)
                .ThenInclude(p => p.GameUids)
            .Include(u => u.Profiles)
                .ThenInclude(p => p.LastUsedRegions)
            .SingleOrDefaultAsync(u => u.Id == (long)userId);

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

            using var scope = m_ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<UserDbContext>();

            // Ensure timestamp is set similar to Mongo implementation
            user.Timestamp = DateTime.UtcNow;

            // Load existing with children
            var existing = await context.Users
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.GameUids)
                .Include(u => u.Profiles)
                    .ThenInclude(p => p.LastUsedRegions)
                .SingleOrDefaultAsync(u => u.Id == (long)user.Id);

            if (existing == null)
            {
                var newModel = MapToModel(user);
                await context.Users.AddAsync(newModel);
            }
            else
            {
                // Update root
                existing.Timestamp = user.Timestamp;

                // Replace profiles to keep parity with document upsert behavior
                // Remove existing children (cascade will handle children but we need explicit for update)
                context.UserProfiles.RemoveRange(existing.Profiles);

                existing.Profiles.Clear();

                var rebuiltProfiles = BuildProfileModels(user, (ulong)existing.Id);
                foreach (var p in rebuiltProfiles)
                {
                    existing.Profiles.Add(p);
                }

                context.Users.Update(existing);
            }

            await context.SaveChangesAsync();
            m_Logger.LogInformation("User {UserId} successfully saved to database", user.Id);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error saving user {UserId} to database", user.Id);
        }
    }

    public async Task<bool> DeleteUserAsync(ulong userId)
    {
        try
        {
            m_Logger.LogInformation("Attempting to delete user {UserId} from database", userId);

            using var scope = m_ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<UserDbContext>();

            var user = await context.Users.FindAsync((long)userId);
            if (user == null)
            {
                m_Logger.LogInformation("Delete user {UserId} result: {Result}", userId, "Not Found");
                return false;
            }

            context.Users.Remove(user);
            var affected = await context.SaveChangesAsync();
            var success = affected > 0;

            m_Logger.LogInformation("Delete user {UserId} result: {Result}", userId, success ? "Deleted" : "Not Found");
            return success;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error deleting user {UserId} from database", userId);
            return false;
        }
    }

    private static UserDto MapToDto(UserModel model)
    {
        var dto = new UserDto
        {
            Id = (ulong)model.Id,
            Timestamp = model.Timestamp,
            Profiles = [.. model.Profiles.Select(p => new UserProfileDto
            {
                ProfileId = (uint)p.ProfileId,
                LtUid = (ulong)p.LtUid,
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
            })]
        };
        return dto;
    }

    private static UserModel MapToModel(UserDto dto)
    {
        var model = new UserModel
        {
            Id = (long)dto.Id,
            Timestamp = dto.Timestamp,
            Profiles = BuildProfileModels(dto, dto.Id)
        };
        return model;
    }

    private static List<UserProfileModel> BuildProfileModels(UserDto dto, ulong userId)
    {
        var profiles = new List<UserProfileModel>();
        if (dto.Profiles == null)
            return profiles;

        foreach (var p in dto.Profiles)
        {
            var profile = new UserProfileModel
            {
                UserId = (long)userId,
                ProfileId = (int)p.ProfileId,
                LtUid = (long)p.LtUid,
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
