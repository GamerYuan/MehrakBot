using G_BuddyCore.Models;
using G_BuddyCore.Services;
using MongoDB.Driver;

namespace G_BuddyCore.Repositories;

public class UserRepository
{
    private readonly IMongoCollection<UserModel> m_Users;

    public UserRepository(MongoDbService mongoDbService)
    {
        m_Users = mongoDbService.Users;
    }

    public async Task<UserModel?> GetUserAsync(ulong userId)
    {
        return await m_Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
    }

    public async Task CreateOrUpdateUserAsync(UserModel user)
    {
        await m_Users.ReplaceOneAsync(
            u => u.Id == user.Id,
            user,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<bool> DeleteUserAsync(ulong userId)
    {
        var result = await m_Users.DeleteOneAsync(u => u.Id == userId);
        return result.DeletedCount > 0;
    }
}
