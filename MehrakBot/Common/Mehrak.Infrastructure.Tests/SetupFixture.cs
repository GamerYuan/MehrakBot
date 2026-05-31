using Mehrak.Infrastructure.Tests.TestUtils;

namespace Mehrak.Infrastructure.Tests;

[SetUpFixture]
internal class SetupFixture
{
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await RedisTestHelper.Instance.InitAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await RedisTestHelper.Instance.DisposeAsync();
    }
}
