namespace Mehrak.Infrastructure.Tests;

[SetUpFixture]
public class TestSetup
{
    private MongoTestHelper m_MongoTestHelper;

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_MongoTestHelper = new MongoTestHelper();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
    }
}
