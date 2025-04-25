#region

using G_BuddyCore.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace G_BuddyCore.Services;

public class MongoDbService
{
    private readonly IMongoDatabase m_Database;
    private readonly ILogger<MongoDbService> m_Logger;

    public MongoDbService(IConfiguration configuration, ILogger<MongoDbService> logger)
    {
        m_Logger = logger;

        var connectionString = configuration["MongoDB:ConnectionString"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        m_Logger.LogInformation("Initializing MongoDB connection to database: {DatabaseName}", databaseName);

        try
        {
            var client = new MongoClient(connectionString);
            m_Database = client.GetDatabase(databaseName);
            m_Logger.LogInformation("MongoDB connection established successfully");
        }
        catch (Exception ex)
        {
            m_Logger.LogCritical(ex, "Failed to connect to MongoDB");
            throw;
        }
    }

    public IMongoCollection<UserModel> Users => m_Database.GetCollection<UserModel>("users");
}
