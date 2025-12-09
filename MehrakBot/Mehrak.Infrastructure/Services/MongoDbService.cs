#region

using Mehrak.Infrastructure.Migrations;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

#endregion

namespace Mehrak.Infrastructure.Services;

[Obsolete]
public class MongoDbService
{
    private readonly IMongoDatabase m_Database;

    public MongoDbService(IMongoDatabase db, ILogger<MongoDbService> logger)
    {
        m_Database = db;
    }

    public IMongoCollection<MongoUserModel> Users => m_Database.GetCollection<MongoUserModel>("users");
    public IMongoCollection<MongoRelicModel> HsrRelics => m_Database.GetCollection<MongoRelicModel>("hsr_relics");
}
