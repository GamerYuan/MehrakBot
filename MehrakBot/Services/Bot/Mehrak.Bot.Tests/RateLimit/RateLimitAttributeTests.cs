using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Services.RateLimit;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Services;

namespace Mehrak.Bot.Tests.RateLimit;

[TestFixture]
public class RateLimitAttributeTests
{
    private Mock<IServiceProvider> m_ServiceProviderMock;
    private Mock<ICommandRateLimitService> m_RateLimitServiceMock;
    private Mock<IUserContext> m_UserContextMock;
    private Mock<User> m_UserMock;
    private RateLimitAttribute<IUserContext> m_Attribute;

    [SetUp]
    public void SetUp()
    {
        m_ServiceProviderMock = new Mock<IServiceProvider>();
        m_RateLimitServiceMock = new Mock<ICommandRateLimitService>();
        m_UserContextMock = new Mock<IUserContext>();

        // Create a JsonUser with the ID we want
        var jsonUser = new JsonUser { Id = 12345ul };

        m_UserMock = new Mock<User>(MockBehavior.Loose, [jsonUser, null!]);

        m_Attribute = new RateLimitAttribute<IUserContext>();

        // Setup service provider to return rate limit service
        m_ServiceProviderMock
            .Setup(x => x.GetService(typeof(ICommandRateLimitService)))
            .Returns(m_RateLimitServiceMock.Object);

        // Setup user context
        m_UserContextMock.Setup(x => x.User).Returns(m_UserMock.Object);

        m_UserMock.Setup(x => x.Id).Returns(12345ul);
    }

    [Test]
    public async Task EnsureCanExecuteAsync_ServiceProviderIsNull_ReturnsFail()
    {
        // Act
        var result = await m_Attribute.EnsureCanExecuteAsync(m_UserContextMock.Object, null);

        // Assert
        Assert.That(result, Is.InstanceOf<IFailResult>());
        var failResult = (IFailResult)result;
        Assert.That(failResult.Message, Is.EqualTo("Rate limiting is temporarily unavailable. Please try again later."));
    }

    [Test]
    public async Task EnsureCanExecuteAsync_RateLimitNotAllowed_ReturnsFail()
    {
        // Arrange
        m_RateLimitServiceMock
            .Setup(x => x.IsAllowedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(false);

        // Act
        var result = await m_Attribute.EnsureCanExecuteAsync(m_UserContextMock.Object, m_ServiceProviderMock.Object);

        // Assert
        Assert.That(result, Is.InstanceOf<IFailResult>());
        var failResult = (IFailResult)result;
        Assert.That(failResult.Message, Is.EqualTo("Used command too frequently! Please try again later"));
        m_RateLimitServiceMock.Verify(x => x.IsAllowedAsync(12345ul), Times.Once);
    }

    [Test]
    public async Task EnsureCanExecuteAsync_RateLimitAllowed_ReturnsSuccess()
    {
        // Arrange
        m_RateLimitServiceMock
            .Setup(x => x.IsAllowedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(true);

        // Act
        var result = await m_Attribute.EnsureCanExecuteAsync(m_UserContextMock.Object, m_ServiceProviderMock.Object);

        // Assert
        Assert.That(result, Is.Not.InstanceOf<IFailResult>());
        m_RateLimitServiceMock.Verify(x => x.IsAllowedAsync(12345ul), Times.Once);
    }
}
