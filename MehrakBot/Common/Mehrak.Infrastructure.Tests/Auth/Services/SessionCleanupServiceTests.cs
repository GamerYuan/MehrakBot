using Mehrak.Infrastructure.Auth.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Infrastructure.Tests.Auth.Services;

[TestFixture]
internal sealed class SessionCleanupServiceTests
{
    [Test]
    public async Task ExecuteAsync_CancellationDuringStartup_ExitsGracefully()
    {
        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        var service = new SessionCleanupService(
            mockScopeFactory.Object,
            Mock.Of<ILogger<SessionCleanupService>>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        mockScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }
}
