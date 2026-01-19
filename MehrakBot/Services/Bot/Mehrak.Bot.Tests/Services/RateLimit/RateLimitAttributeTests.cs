using Mehrak.Bot.Services.RateLimit;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Services;

namespace Mehrak.Bot.Tests.Services.RateLimit;

[TestFixture]
public class RateLimitAttributeTests
{
    private Mock<IServiceProvider> _serviceProviderMock;
    private Mock<ICommandRateLimitService> _rateLimitServiceMock;
    private Mock<IUserContext> _userContextMock;
    private Mock<User> _userMock;
    private RateLimitAttribute<IUserContext> _attribute;

    [SetUp]
    public void SetUp()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _rateLimitServiceMock = new Mock<ICommandRateLimitService>();
        _userContextMock = new Mock<IUserContext>();

        // Create a JsonUser with the ID we want
        var jsonUser = new JsonUser { Id = 12345ul };

        _userMock = new Mock<User>(MockBehavior.Loose, [jsonUser, null!]);

        _attribute = new RateLimitAttribute<IUserContext>();

        // Setup service provider to return rate limit service
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(ICommandRateLimitService)))
            .Returns(_rateLimitServiceMock.Object);

        // Setup user context
        _userContextMock.Setup(x => x.User).Returns(_userMock.Object);

        _userMock.Setup(x => x.Id).Returns(12345ul);
    }

    [Test]
    public async Task EnsureCanExecuteAsync_ServiceProviderIsNull_ReturnsFail()
    {
        // Act
        var result = await _attribute.EnsureCanExecuteAsync(_userContextMock.Object, null);

        // Assert
        Assert.That(result, Is.InstanceOf<IFailResult>());
        var failResult = (IFailResult)result;
        Assert.That(failResult.Message, Is.EqualTo("Rate limiting is temporarily unavailable. Please try again later."));
    }

    [Test]
    public async Task EnsureCanExecuteAsync_RateLimitNotAllowed_ReturnsFail()
    {
        // Arrange
        _rateLimitServiceMock
            .Setup(x => x.IsAllowedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(false);

        // Act
        var result = await _attribute.EnsureCanExecuteAsync(_userContextMock.Object, _serviceProviderMock.Object);

        // Assert
        Assert.That(result, Is.InstanceOf<IFailResult>());
        var failResult = (IFailResult)result;
        Assert.That(failResult.Message, Is.EqualTo("Used command too frequently! Please try again later"));
        _rateLimitServiceMock.Verify(x => x.IsAllowedAsync(12345ul), Times.Once);
    }

    [Test]
    public async Task EnsureCanExecuteAsync_RateLimitAllowed_ReturnsSuccess()
    {
        // Arrange
        _rateLimitServiceMock
            .Setup(x => x.IsAllowedAsync(It.IsAny<ulong>()))
            .ReturnsAsync(true);

        // Act
        var result = await _attribute.EnsureCanExecuteAsync(_userContextMock.Object, _serviceProviderMock.Object);

        // Assert
        Assert.That(result, Is.Not.InstanceOf<IFailResult>());
        _rateLimitServiceMock.Verify(x => x.IsAllowedAsync(12345ul), Times.Once);
    }
}
