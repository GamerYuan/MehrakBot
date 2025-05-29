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

public class MongoTestHelper : IDisposable
{
    public MongoDbService MongoDbService { get; }

    private readonly MongoDbRunner m_MongoRunner;

    static MongoTestHelper()
    {
        BsonSerializer.RegisterSerializer(new EnumSerializer<GameName>(BsonType.String));
    }

    public MongoTestHelper()
    {
        m_MongoRunner = MongoDbRunner.Start();

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = m_MongoRunner.ConnectionString,
            ["MongoDB:DatabaseName"] = "TestDb"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        MongoDbService = new MongoDbService(config, NullLogger<MongoDbService>.Instance);
    }

    public void Dispose()
    {
        m_MongoRunner.Dispose();
    }
}