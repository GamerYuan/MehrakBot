namespace Mehrak.Infrastructure.Tests.IntegrationTests;

[SetUpFixture]
internal class SetupFixture
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresTestHelper.Instance.InitAsync();
        await RedisTestHelper.Instance.InitAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await RedisTestHelper.Instance.DisposeAsync();
        await PostgresTestHelper.Instance.DisposeAsync();
    }
}
