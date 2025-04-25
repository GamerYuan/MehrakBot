using MongoDB.Driver;
using G_BuddyCore.Models;
using Microsoft.Extensions.Configuration;

namespace G_BuddyCore.Services;

public class MongoDbService
{
    private readonly IMongoDatabase m_Database;

    public MongoDbService(IConfiguration configuration)
    {
        var connectionString = configuration["MongoDB:ConnectionString"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        var client = new MongoClient(connectionString);
        m_Database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<UserModel> Users => m_Database.GetCollection<UserModel>("users");
}
