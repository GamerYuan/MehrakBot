#region

#endregion

using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Repositories;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Mehrak.Infrastructure.Tests;

public sealed class MongoTestHelper : IDisposable
{
    public static MongoTestHelper Instance { get; private set; } = null!;
    public MongoDbService MongoDbService { get; }
    public IImageRepository ImageRepository { get; }

    private readonly MongoDbRunner m_MongoRunner;

    private ulong m_TestUserId = 1_000_000_000;

    public MongoTestHelper()
    {
        BsonSerializer.RegisterSerializer(new EnumSerializer<Game>(BsonType.String));
        m_MongoRunner = MongoDbRunner.Start(logger: NullLogger<MongoDbRunner>.Instance);

        MongoClient mongoClient = new(m_MongoRunner.ConnectionString);
        IMongoDatabase database = mongoClient.GetDatabase("TestDb");

        MongoDbService = new MongoDbService(database, NullLogger<MongoDbService>.Instance);
        ImageRepository = new ImageRepository(MongoDbService,
            NullLogger<ImageRepository>.Instance);

        Instance = this;
    }

    public ulong GetUniqueUserId()
    {
        return Interlocked.Increment(ref m_TestUserId);
    }

    public void Dispose()
    {
        m_MongoRunner.Dispose();
    }
}
