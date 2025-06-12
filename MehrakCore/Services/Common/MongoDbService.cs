#region

using MehrakCore.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

#endregion

namespace MehrakCore.Services.Common;

public class MongoDbService
{
    private readonly IMongoDatabase m_Database;

    public MongoDbService(IConfiguration configuration, ILogger<MongoDbService> logger)
    {
        var connectionString = configuration["MongoDB:ConnectionString"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        logger.LogInformation("Initializing MongoDB connection to database: {DatabaseName}", databaseName);

        try
        {
            var client = new MongoClient(connectionString);
            m_Database = client.GetDatabase(databaseName);
            logger.LogInformation("MongoDB connection established successfully");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to connect to MongoDB");
            throw;
        }
    }

    public IMongoCollection<UserModel> Users => m_Database.GetCollection<UserModel>("users");

    public GridFSBucket Bucket => new(m_Database);

    public async Task<bool> IsConnected()
    {
        try
        {
            _ = await m_Database.RunCommandAsync((Command<BsonDocument>)"{ping:1}") != null;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
