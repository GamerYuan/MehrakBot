#region

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Mehrak.Bot.Authentication;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Authentication;

[Parallelizable(ParallelScope.Self)]
public class AuthenticationMiddlewareServiceTests
{
    private Mock<ICacheService> m_MockCacheService = null!;
    private Mock<IEncryptionService> m_MockEncryptionService = null!;
    private Mock<ILogger<AuthenticationMiddlewareService>> m_MockLogger = null!;
    private AuthenticationMiddlewareService m_Service = null!;
    private TestDbContextFactory? m_DbFactory;

    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const uint TestProfileId = 1U;
    private const string TestPassphrase = "test-passphrase-123";
    private const string TestLToken = "test-ltoken-value";
    private const string TestEncryptedToken = "encrypted-token-base64";

    [SetUp]
    public void Setup()
    {
        m_MockCacheService = new Mock<ICacheService>();
        m_MockEncryptionService = new Mock<IEncryptionService>();
        m_MockLogger = new Mock<ILogger<AuthenticationMiddlewareService>>();

        InitializeService();
    }

    [TearDown]
    public void TearDown()
    {
        m_MockCacheService.Reset();
        m_MockEncryptionService.Reset();
        m_MockLogger.Reset();
        m_DbFactory?.Dispose();
    }

    #region GetAuthenticationAsync Tests

    [Test]
    public async Task GetAuthenticationAsync_UserNotFound_ReturnsNotFound()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = new ModalInteraction(new JsonInteraction
        {
            Token = "sample_token",
            Data = new JsonInteractionData
            {
                Components = []
            },
            User = new JsonUser
            {
                Id = TestUserId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        }, null!, null!, new RestClient());
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        // Act
        var result = await m_Service.GetAuthenticationAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("User account not found"));
            Assert.That(result.Status, Is.EqualTo(AuthStatus.NotFound));
        });
    }

    [Test]
    public async Task GetAuthenticationAsync_ProfileNotFound_ReturnsNotFound()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = new ModalInteraction(new JsonInteraction
        {
            Token = "sample_token",
            Data = new JsonInteractionData
            {
                Components = []
            },
            User = new JsonUser
            {
                Id = TestUserId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        }, null!, null!, new RestClient());
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);
        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        InitializeService(context =>
        {
            context.Users.Add(new UserModel
            {
                Id = (long)TestUserId,
                Timestamp = DateTime.UtcNow
            });
        });

        // Act
        var result = await m_Service.GetAuthenticationAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("No profiles found"));
            Assert.That(result.Status, Is.EqualTo(AuthStatus.NotFound));
        });
    }

    [Test]
    public async Task GetAuthenticationAsync_TokenInCache_ReturnsSuccess()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = new ModalInteraction(new JsonInteraction
        {
            Token = "sample_token",
            Data = new JsonInteractionData
            {
                Components = []
            },
            User = new JsonUser
            {
                Id = TestUserId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        }, null!, (_, _, _, _, _) => Task.FromResult<InteractionCallbackResponse?>(null), new RestClient());
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        var cacheKey = CacheKeys.BotLToken(TestUserId, TestLtUid);

        InitializeService(context =>
        {
            context.Users.Add(BuildUserModel(TestUserId, TestLtUid, TestEncryptedToken));
        });

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync(TestLToken);

        // Act
        var result = await m_Service.GetAuthenticationAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Success));
            Assert.That(result.User, Is.Not.Null);
            Assert.That(result.User!.Id, Is.EqualTo(TestUserId));
            Assert.That(result.User.Profiles?.FirstOrDefault()?.LtUid, Is.EqualTo(TestLtUid));
        });

        m_MockCacheService.Verify(x => x.GetAsync<string>(cacheKey), Times.Once);
        m_MockEncryptionService.Verify(x => x.Decrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task GetAuthenticationAsync_DecryptionSuccess_ReturnsSuccessAndCachesToken()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = new ModalInteraction(new JsonInteraction
        {
            Token = "sample_token",
            Data = new JsonInteractionData
            {
                Components = []
            },
            User = new JsonUser
            {
                Id = TestUserId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        }, null!, (_, _, _, _, _) => Task.FromResult<InteractionCallbackResponse?>(null), new RestClient());
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);
        var cacheKey = CacheKeys.BotLToken(TestUserId, TestLtUid);

        InitializeService(context =>
        {
            context.Users.Add(BuildUserModel(TestUserId, TestLtUid, TestEncryptedToken));
        });

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null);

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Returns(Task.CompletedTask);

        m_MockEncryptionService
            .Setup(x => x.Decrypt(TestEncryptedToken, TestPassphrase))
            .Returns(TestLToken);

        var authTask = m_Service.GetAuthenticationAsync(request);
        var guid = await WaitForAuthenticationGuidAsync(m_Service);

        var authResponse = new AuthenticationResponse(TestUserId, guid, TestPassphrase, mockContext.Object);
        var notifyResult = m_Service.NotifyAuthenticate(authResponse);

        var result = await authTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(notifyResult, Is.True);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.Context, Is.EqualTo(mockContext.Object));
            Assert.That(result.User, Is.Not.Null);
            Assert.That(result.User!.Id, Is.EqualTo(TestUserId));
        });

        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(entry =>
                entry.Key == cacheKey &&
                entry.Value == TestLToken &&
                entry.ExpirationTime == TimeSpan.FromMinutes(10))),
            Times.Once);
    }

    #endregion

    #region NotifyAuthenticate Tests

    [Test]
    public void NotifyAuthenticate_WithInvalidGuid_ReturnsFalse()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var authResponse = new AuthenticationResponse(
            TestUserId,
            "invalid-guid-12345",
            TestPassphrase,
            mockContext.Object);

        // Act
        var result = m_Service.NotifyAuthenticate(authResponse);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task NotifyAuthenticate_WithValidGuid_ReturnsTrue()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = new ModalInteraction(new JsonInteraction
        {
            Token = "sample_token",
            Data = new JsonInteractionData
            {
                Components = []
            },
            User = new JsonUser
            {
                Id = TestUserId
            },
            Channel = new JsonChannel
            {
                Id = 987654321UL,
                Type = ChannelType.TextGuildChannel
            },
            Entitlements = []
        }, null!, null!, new RestClient());
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        InitializeService(context =>
        {
            context.Users.Add(BuildUserModel(TestUserId, TestLtUid, TestEncryptedToken));
        });

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        _ = Task.Run(() => m_Service.GetAuthenticationAsync(request));

        await Task.Delay(100);

        var authResponse = new AuthenticationResponse(
            TestUserId,
            Guid.NewGuid().ToString(),
            TestPassphrase,
            mockContext.Object);

        // Act
        var result = m_Service.NotifyAuthenticate(authResponse);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region AuthenticationResult Tests

    [Test]
    public void AuthenticationResult_Success_CreatesValidResult()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var user = new UserDto { Id = TestUserId };

        // Act
        var result = AuthenticationResult.Success(
            TestUserId,
            TestLtUid,
            TestLToken,
            user,
            mockContext.Object);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Success));
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.User, Is.EqualTo(user));
            Assert.That(result.Context, Is.EqualTo(mockContext.Object));
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void AuthenticationResult_Failure_CreatesValidResult()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        const string errorMessage = "Test error message";

        // Act
        var result = AuthenticationResult.Failure(mockContext.Object, errorMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Failure));
            Assert.That(result.ErrorMessage, Is.EqualTo(errorMessage));
            Assert.That(result.Context, Is.EqualTo(mockContext.Object));
            Assert.That(result.LToken, Is.Null);
            Assert.That(result.User, Is.Null);
        });
    }

    [Test]
    public void AuthenticationResult_Timeout_CreatesValidResult()
    {
        // Act
        var result = AuthenticationResult.Timeout();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Timeout));
            Assert.That(result.ErrorMessage, Is.EqualTo("Authentication timed out"));
            Assert.That(result.Context, Is.Null);
            Assert.That(result.LToken, Is.Null);
            Assert.That(result.User, Is.Null);
        });
    }

    #endregion

    #region AuthenticationRequest Tests

    [Test]
    public void AuthenticationRequest_Constructor_SetsProperties()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();

        // Act
        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(request.Context, Is.EqualTo(mockContext.Object));
            Assert.That(request.ProfileId, Is.EqualTo(TestProfileId));
        });
    }

    #endregion

    #region AuthenticationResponse Tests

    [Test]
    public void AuthenticationResponse_Constructor_SetsProperties()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        const string guid = "test-guid-123";

        // Act
        var response = new AuthenticationResponse(
            TestUserId,
            guid,
            TestPassphrase,
            mockContext.Object);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.UserId, Is.EqualTo(TestUserId));
            Assert.That(response.Guid, Is.EqualTo(guid));
            Assert.That(response.Passphrase, Is.EqualTo(TestPassphrase));
            Assert.That(response.Context, Is.EqualTo(mockContext.Object));
        });
    }

    #endregion

    #region Helpers

    private void InitializeService(Action<UserDbContext>? seed = null)
    {
        m_DbFactory?.Dispose();
        m_DbFactory = new TestDbContextFactory(seed: seed);
        m_Service = new AuthenticationMiddlewareService(
            m_MockCacheService.Object,
            m_MockEncryptionService.Object,
            m_DbFactory.ScopeFactory,
            m_MockLogger.Object);
    }

    private static UserModel BuildUserModel(ulong userId, ulong ltUid, string ltoken, int profileId = (int)TestProfileId)
    {
        var user = new UserModel
        {
            Id = (long)userId,
            Timestamp = DateTime.UtcNow
        };

        user.Profiles.Add(new UserProfileModel
        {
            User = user,
            UserId = user.Id,
            ProfileId = profileId,
            LtUid = (long)ltUid,
            LToken = ltoken
        });

        return user;
    }

    private static async Task<string> WaitForAuthenticationGuidAsync(AuthenticationMiddlewareService service,
        TimeSpan timeout = default)
    {
        var field = typeof(AuthenticationMiddlewareService)
            .GetField("m_CurrentRequests", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
            throw new InvalidOperationException("Unable to access m_CurrentRequests");

        var dictionary = (ConcurrentDictionary<string, byte>)field.GetValue(service)!;
        var sw = Stopwatch.StartNew();
        var cutoff = timeout == default ? TimeSpan.FromSeconds(5) : timeout;

        while (sw.Elapsed < cutoff)
        {
            if (dictionary.Keys.FirstOrDefault() is { } guid)
                return guid;

            await Task.Delay(25);
        }

        throw new TimeoutException("Authentication request was not registered in time.");
    }

    #endregion
}
