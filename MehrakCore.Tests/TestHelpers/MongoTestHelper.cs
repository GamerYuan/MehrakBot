#region

using MehrakCore.Models;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

#endregion

namespace MehrakCore.Tests.TestHelpers;

public sealed class MongoTestHelper : IDisposable
{
    public static MongoTestHelper Instance { get; private set; } = null!;
    public MongoDbService MongoDbService { get; }

    private readonly MongoDbRunner m_MongoRunner;

    public MongoTestHelper()
    {
        BsonSerializer.RegisterSerializer(new EnumSerializer<GameName>(BsonType.String));
        m_MongoRunner = MongoDbRunner.Start(logger: NullLogger<MongoDbRunner>.Instance);

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = m_MongoRunner.ConnectionString,
            ["MongoDB:DatabaseName"] = "TestDb"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        MongoDbService = new MongoDbService(config, NullLogger<MongoDbService>.Instance);

        Instance = this;
    }

    public void Dispose()
    {
        m_MongoRunner.Dispose();
    }
}
