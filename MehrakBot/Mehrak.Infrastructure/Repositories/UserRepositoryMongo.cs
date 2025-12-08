#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Models;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Repositories;

public class UserRepositoryMongo : IUserRepository
{
    private readonly IMongoCollection<MongoUserModel> m_Users;
    private readonly ILogger<UserRepositoryMongo> m_Logger;

    public UserRepositoryMongo(MongoDbService mongoDbService, ILogger<UserRepositoryMongo> logger)
    {
        m_Users = mongoDbService.Users;
        m_Logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(ulong userId)
    {
        m_Logger.LogDebug("Retrieving user {UserId} from database", userId);
        var user = await m_Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
        m_Logger.LogDebug("User {UserId} retrieval result: {Result}", userId, user != null ? "Found" : "Not Found");
        return user;
    }

    public async Task CreateOrUpdateUserAsync(UserDto user)
    {
        try
        {
            m_Logger.LogInformation("Creating or updating user {UserId} in database", user.Id);
            user.Timestamp = DateTime.UtcNow;

            await m_Users.ReplaceOneAsync(
                u => u.Id == user.Id,
                user,
                new ReplaceOptions { IsUpsert = true });

            m_Logger.LogInformation("User {UserId} successfully saved to database", user.Id);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error saving user {UserId} to database", user.Id);
            throw;
        }
    }

    public async Task<bool> DeleteUserAsync(ulong userId)
    {
        try
        {
            m_Logger.LogInformation("Attempting to delete user {UserId} from database", userId);
            var result = await m_Users.DeleteOneAsync(u => u.Id == userId);
            var success = result.DeletedCount > 0;

            m_Logger.LogInformation("Delete user {UserId} result: {Result}",
                userId, success ? "Deleted" : "Not Found");

            return success;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error deleting user {UserId} from database", userId);
            throw;
        }
    }
}
