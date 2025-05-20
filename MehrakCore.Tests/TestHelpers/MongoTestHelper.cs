#region

using MehrakCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using Moq;

#endregion

namespace MehrakCore.Tests.TestHelpers;

public class MongoTestHelper : IDisposable
{
    private readonly Mock<MongoDbService> m_DbMock;
    public MongoDbService MongoDbService => m_DbMock.Object;

    private readonly MongoDbRunner m_MongoRunner;

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

        m_DbMock = new Mock<MongoDbService>(config, NullLogger<MongoDbService>.Instance);
    }

    public void Dispose()
    {
        m_MongoRunner.Dispose();
    }
}
