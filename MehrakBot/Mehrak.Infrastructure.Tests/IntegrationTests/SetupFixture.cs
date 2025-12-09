namespace Mehrak.Infrastructure.Tests.IntegrationTests;

[SetUpFixture]
internal class SetupFixture
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresTestHelper.Instance.InitAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresTestHelper.Instance.DisposeAsync();
    }
}
