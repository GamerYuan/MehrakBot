#region

using MehrakCore.Models;
using MongoDB.Driver;

#endregion

namespace MehrakCore.Utility;

public class UserModelMigration
{
    private readonly IMongoCollection<UserModel> m_UserCollection;

    public UserModelMigration(IMongoDatabase database)
    {
        m_UserCollection = database.GetCollection<UserModel>("users"); // Use your actual collection name
    }

    public async Task MigrateUsersToProfileStructure()
    {
        var filter = Builders<UserModel>.Filter.Empty;
        var users = await m_UserCollection.Find(filter).ToListAsync();

        var updates = new List<WriteModel<UserModel>>();

        foreach (var user in users)
        {
            // Skip users that have already been migrated
            if (user.Profiles != null)
                continue;

            // Create a new profile from existing user data
            var profile = new UserProfile
            {
                ProfileId = 1, // Default profile ID
                LtUid = user.LtUid,
                LToken = user.LToken,
                GameUids = user.GameUids,
                LastUsedRegions = new Dictionary<GameName, Regions>() // Initialize empty
            };

            // Create update definition
            var update = Builders<UserModel>.Update
                .Set(u => u.Profiles, new[] { profile })
                .Unset(u => u.LtUid)
                .Unset(u => u.LToken)
                .Unset(u => u.GameUids);

            var updateModel = new UpdateOneModel<UserModel>(
                Builders<UserModel>.Filter.Eq(u => u.Id, user.Id),
                update);

            updates.Add(updateModel);
        }

        if (updates.Count > 0)
            // Execute all updates in a bulk operation
            await m_UserCollection.BulkWriteAsync(updates);
    }

    private Regions GetRegionFromIdentifier(string identifier)
    {
        return identifier switch
        {
            "os_cht" => Regions.Sar,
            "os_usa" => Regions.America,
            "os_euro" => Regions.Europe,
            "os_asia" => Regions.Asia,
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
