#region

using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Migrations;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

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
    public IMongoCollection<MongoCharacterModel> Characters => m_Database.GetCollection<MongoCharacterModel>("characters");
    public IMongoCollection<AliasModel> Aliases => m_Database.GetCollection<AliasModel>("aliases");
    public IMongoCollection<CodeRedeemModel> Codes => m_Database.GetCollection<CodeRedeemModel>("codes");
    public IMongoCollection<MongoRelicModel> HsrRelics => m_Database.GetCollection<MongoRelicModel>("hsr_relics");

    public GridFSBucket Bucket => new(m_Database);
}
