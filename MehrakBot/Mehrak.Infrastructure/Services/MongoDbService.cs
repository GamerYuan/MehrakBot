using Mehrak.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Mehrak.Infrastructure.Services;

public class MongoDbService
{
    private readonly IMongoDatabase m_Database;

    public MongoDbService(IConfiguration configuration, ILogger<MongoDbService> logger)
    {
        string? connectionString = configuration["MongoDB:ConnectionString"];
        string? databaseName = configuration["MongoDB:DatabaseName"];

        logger.LogInformation("Initializing MongoDB connection to database: {DatabaseName}", databaseName);

        try
        {
            MongoClient client = new(connectionString);
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
    public IMongoCollection<CharacterModel> Characters => m_Database.GetCollection<CharacterModel>("characters");
    public IMongoCollection<AliasModel> Aliases => m_Database.GetCollection<AliasModel>("aliases");
    public IMongoCollection<CodeRedeemModel> Codes => m_Database.GetCollection<CodeRedeemModel>("codes");
    public IMongoCollection<HsrRelicModel> HsrRelics => m_Database.GetCollection<HsrRelicModel>("hsr_relics");

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
