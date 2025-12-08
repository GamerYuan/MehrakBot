#region

using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Models;
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
    public IMongoCollection<CharacterModel> Characters => m_Database.GetCollection<CharacterModel>("characters");
    public IMongoCollection<AliasModel> Aliases => m_Database.GetCollection<AliasModel>("aliases");
    public IMongoCollection<CodeRedeemModel> Codes => m_Database.GetCollection<CodeRedeemModel>("codes");
    public IMongoCollection<HsrRelicModel> HsrRelics => m_Database.GetCollection<HsrRelicModel>("hsr_relics");

    public GridFSBucket Bucket => new(m_Database);
}
