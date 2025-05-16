#region

using MehrakCore.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

#endregion

namespace MehrakCore.Services;

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
}
