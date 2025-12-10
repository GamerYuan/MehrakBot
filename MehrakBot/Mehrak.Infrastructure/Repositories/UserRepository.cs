using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Extensions;
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

        var dto = user.ToDto();
        m_Logger.LogDebug("User {UserId} retrieval result: {Result}", userId, "Found");
        return dto;
    }

    public async Task<bool> CreateOrUpdateUserAsync(UserDto user)
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
                var newModel = user.ToUserModel();
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

                var rebuiltProfiles = user.BuildProfileModels((ulong)existing.Id);
                foreach (var p in rebuiltProfiles)
                {
                    existing.Profiles.Add(p);
                }

                context.Users.Update(existing);
            }

            await context.SaveChangesAsync();
            m_Logger.LogInformation("User {UserId} successfully saved to database", user.Id);
            return true;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error saving user {UserId} to database", user.Id);
            return false;
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
}
