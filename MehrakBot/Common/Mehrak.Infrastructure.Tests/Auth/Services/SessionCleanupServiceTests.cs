using Mehrak.Domain.Auth;
using Mehrak.Infrastructure.Auth.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Infrastructure.Tests.Auth.Services;

[TestFixture]
internal sealed class SessionCleanupServiceTests
{
    [Test]
    public async Task ExecuteAsync_Cancellation_ExitsGracefully()
    {
        var mockSessionService = new Mock<IDashboardSessionService>();
        mockSessionService.Setup(s => s.CleanupExpiredSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IDashboardSessionService)))
            .Returns(mockSessionService.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var service = new SessionCleanupService(
            mockScopeFactory.Object,
            Mock.Of<ILogger<SessionCleanupService>>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrowAsync(() => service.StartAsync(cts.Token));
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ExecuteAsync_CancellationDuringStartup_ExitsGracefully()
    {
        var mockSessionService = new Mock<IDashboardSessionService>();
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
