#region

using System.Reflection;
using System.Text.RegularExpressions;
using Mehrak.Bot.Authentication;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Tests.Authentication;

/// <summary>
/// Integration tests for AuthenticationMiddlewareService that test the full authentication flow
/// with real encryption service and coordination between async components.
/// </summary>
[Parallelizable(ParallelScope.Self | ParallelScope.Children)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public partial class AuthenticationMiddlewareServiceIntegrationTests
{
    private Mock<ICacheService> m_MockCacheService = null!;
    private Mock<IUserRepository> m_MockUserRepository = null!;
    private CookieEncryptionService m_EncryptionService = null!;
    private AuthenticationMiddlewareService m_Service = null!;
    private DiscordTestHelper m_DiscordHelper = null!;

    private ulong m_TestUserId;
    private const ulong TestLtUid = 987654321UL;
    private const uint TestProfileId = 1U;
    private const string TestPassphrase = "test-passphrase-123";
    private const string TestLToken = "test-ltoken-value";

    [SetUp]
    public void Setup()
    {
        m_MockCacheService = new Mock<ICacheService>();
        m_MockUserRepository = new Mock<IUserRepository>();
        m_EncryptionService = new CookieEncryptionService(NullLogger<CookieEncryptionService>.Instance);
        m_DiscordHelper = new DiscordTestHelper();
        m_DiscordHelper.SetupRequestCapture();

        m_Service = new AuthenticationMiddlewareService(
            m_MockCacheService.Object,
            m_EncryptionService,
            m_MockUserRepository.Object,
            NullLogger<AuthenticationMiddlewareService>.Instance);

        m_TestUserId = (ulong)new Random(DateTime.UtcNow.Microsecond).NextInt64();
    }

    [TearDown]
    public void TearDown()
    {
        m_MockCacheService.Reset();
        m_MockUserRepository.Reset();
        m_DiscordHelper?.Dispose();
    }

    #region Full Authentication Flow Tests

    [Test]
    public async Task GetAuthenticationAsync_FullFlow_CorrectPassphrase_ReturnsSuccessAndCachesToken()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        // Encrypt the token with the test passphrase
        var encryptedToken = m_EncryptionService.Encrypt(TestLToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        var cacheKey = $"bot:ltoken:{m_TestUserId}:{TestLtUid}";

        m_MockUserRepository
            .Setup(x => x.GetUserAsync(m_TestUserId))
            .ReturnsAsync(user);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null);

        var guidCaptured = new TaskCompletionSource<string>();

        // Capture GUID from modal response
        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var guidMatch = ModalGuidRegex().Match(responseData);
            Console.WriteLine(nameof(GetAuthenticationAsync_FullFlow_CorrectPassphrase_ReturnsSuccessAndCachesToken));
            if (guidMatch.Success) guidCaptured.SetResult(guidMatch.Groups[1].Value);
        });

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Returns(Task.CompletedTask);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        // Act - Start the authentication process
        var authTask = m_Service.GetAuthenticationAsync(request);

        // Wait for the GUID to be captured
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Simulate user providing correct passphrase through modal
        var authResponse = new AuthenticationResponse(
            m_TestUserId,
            guid,
            TestPassphrase,
            mockContext.Object);

        var notifyResult = m_Service.NotifyAuthenticate(authResponse);

        // Wait for authentication to complete
        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(notifyResult, Is.True, "NotifyAuthenticate should return true for valid GUID");
            Assert.That(result.IsSuccess, Is.True, "Authentication should succeed");
            Assert.That(result.LToken, Is.EqualTo(TestLToken), "Decrypted token should match original");
            Assert.That(result.UserId, Is.EqualTo(m_TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.User, Is.EqualTo(user));
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Success));
            Assert.That(result.Context, Is.EqualTo(mockContext.Object));
        });

        // Verify the token was cached
        m_MockCacheService.Verify(
            x => x.SetAsync(It.Is<ICacheEntry<string>>(e =>
                e.Key == cacheKey &&
                e.Value == TestLToken &&
                e.ExpirationTime == TimeSpan.FromMinutes(10))),
            Times.Once);
    }

    [Test]
    public async Task GetAuthenticationAsync_FullFlow_WrongPassphrase_ReturnsFailure()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        // Encrypt the token with the correct passphrase
        var encryptedToken = m_EncryptionService.Encrypt(TestLToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        var cacheKey = $"ltoken:{m_TestUserId}:{TestLtUid}";

        m_MockUserRepository
            .Setup(x => x.GetUserAsync(m_TestUserId))
            .ReturnsAsync(user);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null);

        var guidCaptured = new TaskCompletionSource<string>();

        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var guidMatch = ModalGuidRegex().Match(responseData);
            Console.WriteLine(nameof(GetAuthenticationAsync_FullFlow_WrongPassphrase_ReturnsFailure));
            if (guidMatch.Success) guidCaptured.SetResult(guidMatch.Groups[1].Value);
        });

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        // Act - Start the authentication process
        var authTask = m_Service.GetAuthenticationAsync(request);

        // Wait for the GUID to be captured
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Simulate user providing WRONG passphrase
        var authResponse = new AuthenticationResponse(
            m_TestUserId,
            guid,
            "wrong-passphrase-456",
            mockContext.Object);

        var notifyResult = m_Service.NotifyAuthenticate(authResponse);

        // Wait for authentication to complete
        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(notifyResult, Is.True, "NotifyAuthenticate should return true for valid GUID");
            Assert.That(result.IsSuccess, Is.False, "Authentication should fail with wrong passphrase");
            Assert.That(result.ErrorMessage, Does.Contain("Incorrect passphrase"));
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Failure));
            Assert.That(result.Context, Is.EqualTo(mockContext.Object));
        });

        // Verify the token was NOT cached
        m_MockCacheService.Verify(
            x => x.SetAsync(It.IsAny<ICacheEntry<string>>()),
            Times.Never);
    }

    [Test]
    public async Task GetAuthenticationAsync_FullFlow_NoResponse_TimesOut()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var encryptedToken = m_EncryptionService.Encrypt(TestLToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        var cacheKey = $"ltoken:{m_TestUserId}:{TestLtUid}";

        m_MockUserRepository
            .Setup(x => x.GetUserAsync(m_TestUserId))
            .ReturnsAsync(user);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        var service = new AuthenticationMiddlewareService(
            m_MockCacheService.Object,
            m_EncryptionService,
            m_MockUserRepository.Object,
            NullLogger<AuthenticationMiddlewareService>.Instance);

        var prop = service.GetType()
            .GetProperty("TimeoutMinutes", BindingFlags.NonPublic | BindingFlags.Instance);
        prop?.SetValue(service, 0.1f);

        // Act - Start authentication but don't notify (simulate user not responding)
        var result = await service.GetAuthenticationAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "Authentication should fail on timeout");
            Assert.That(result.Status, Is.EqualTo(AuthStatus.Timeout));
            Assert.That(result.ErrorMessage, Is.EqualTo("Authentication timed out"));
            Assert.That(result.Context, Is.Null, "Context should be null on timeout");
        });

        // Verify the token was NOT cached
        m_MockCacheService.Verify(
            x => x.SetAsync(It.IsAny<ICacheEntry<string>>()),
            Times.Never);
    }

    [Test]
    public async Task GetAuthenticationAsync_FullFlow_MultiplePassphraseAttempts_OnlyFirstSucceeds()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var encryptedToken = m_EncryptionService.Encrypt(TestLToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        var cacheKey = $"ltoken:{m_TestUserId}:{TestLtUid}";

        m_MockUserRepository
            .Setup(x => x.GetUserAsync(m_TestUserId))
            .ReturnsAsync(user);

        m_MockCacheService
            .Setup(x => x.GetAsync<string>(cacheKey))
            .ReturnsAsync((string?)null);

        var guidCaptured = new TaskCompletionSource<string>();

        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var guidMatch = ModalGuidRegex().Match(responseData);
            Console.WriteLine(nameof(GetAuthenticationAsync_FullFlow_MultiplePassphraseAttempts_OnlyFirstSucceeds));
            if (guidMatch.Success) guidCaptured.SetResult(guidMatch.Groups[1].Value);
        });

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Returns(Task.CompletedTask);

        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

        // Act
        var authTask = m_Service.GetAuthenticationAsync(request);
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Try to notify twice with the same GUID
        var authResponse1 = new AuthenticationResponse(m_TestUserId, guid, TestPassphrase, mockContext.Object);
        var authResponse2 = new AuthenticationResponse(m_TestUserId, guid, "different-passphrase", mockContext.Object);

        var notify1 = m_Service.NotifyAuthenticate(authResponse1);
        var notify2 = m_Service.NotifyAuthenticate(authResponse2);

        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(notify1, Is.True, "First notification should succeed");
            Assert.That(notify2, Is.False, "Second notification should fail (GUID already used)");
            Assert.That(result.IsSuccess, Is.True, "Should use first passphrase");
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
        });
    }

    #endregion

    #region Concurrent Authentication Tests

    [Test]
    public async Task GetAuthenticationAsync_MultipleConcurrentRequests_EachHandledIndependently()
    {
        // Arrange - Create two separate authentication flows
        var helper1 = new DiscordTestHelper();
        var helper2 = new DiscordTestHelper();

        try
        {
            var context1 = new Mock<IInteractionContext>();
            var interaction1 = helper1.CreateModalInteraction(m_TestUserId);
            context1.SetupGet(x => x.Interaction).Returns(() => interaction1);

            var context2 = new Mock<IInteractionContext>();
            var interaction2 = helper2.CreateModalInteraction(m_TestUserId + 1);
            context2.SetupGet(x => x.Interaction).Returns(() => interaction2);

            var encryptedToken1 = m_EncryptionService.Encrypt("token1", "pass1");
            var encryptedToken2 = m_EncryptionService.Encrypt("token2", "pass2");

            var profile1 = new UserProfileDto { ProfileId = 1, LtUid = 111, LToken = encryptedToken1 };
            var profile2 = new UserProfileDto { ProfileId = 2, LtUid = 222, LToken = encryptedToken2 };

            var user1 = new UserDto { Id = m_TestUserId, Profiles = [profile1] };
            var user2 = new UserDto { Id = m_TestUserId + 1, Profiles = [profile2] };

            m_MockUserRepository.Setup(x => x.GetUserAsync(m_TestUserId)).ReturnsAsync(user1);
            m_MockUserRepository.Setup(x => x.GetUserAsync(m_TestUserId + 1)).ReturnsAsync(user2);

            m_MockCacheService.Setup(x => x.GetAsync<string>(It.IsAny<string>())).ReturnsAsync((string?)null);
            m_MockCacheService.Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>())).Returns(Task.CompletedTask);

            var guid1Captured = new TaskCompletionSource<string>();
            var guid2Captured = new TaskCompletionSource<string>();

            // Capture GUIDs from both interactions
            _ = Task.Run(async () =>
            {
                var responseData = string.Empty;
                do
                {
                    responseData = await helper1.ExtractInteractionResponseDataAsync();
                    await Task.Delay(50);
                } while (string.IsNullOrEmpty(responseData));

                var match1 = ModalGuidRegex().Match(responseData);
                Console.WriteLine(nameof(GetAuthenticationAsync_MultipleConcurrentRequests_EachHandledIndependently) +
                                  " 1");
                if (match1.Success) guid1Captured.SetResult(match1.Groups[1].Value);
            });

            _ = Task.Run(async () =>
            {
                var responseData = string.Empty;
                do
                {
                    responseData = await helper2.ExtractInteractionResponseDataAsync();
                    await Task.Delay(50);
                } while (string.IsNullOrEmpty(responseData));

                var match2 = ModalGuidRegex().Match(responseData);
                if (match2.Success) guid2Captured.SetResult(match2.Groups[1].Value);
            });

            // Act - Start both authentication flows concurrently
            var authTask1 = m_Service.GetAuthenticationAsync(new AuthenticationRequest(context1.Object, 1));
            var authTask2 = m_Service.GetAuthenticationAsync(new AuthenticationRequest(context2.Object, 2));

            var guid1 = await guid1Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var guid2 = await guid2Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Notify both with correct passphrases
            m_Service.NotifyAuthenticate(new AuthenticationResponse(m_TestUserId, guid1, "pass1", context1.Object));
            m_Service.NotifyAuthenticate(new AuthenticationResponse(m_TestUserId + 1, guid2, "pass2", context2.Object));

            var result1 = await authTask1;
            var result2 = await authTask2;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result1.IsSuccess, Is.True);
                Assert.That(result1.LToken, Is.EqualTo("token1"));
                Assert.That(result2.IsSuccess, Is.True);
                Assert.That(result2.LToken, Is.EqualTo("token2"));
            });
        }
        finally
        {
            helper1.Dispose();
            helper2.Dispose();
        }
    }

    #endregion

    #region Encryption Edge Cases

    [Test]
    public async Task GetAuthenticationAsync_EmptyToken_SuccessfullyDecrypts()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var emptyToken = "";
        var encryptedToken = m_EncryptionService.Encrypt(emptyToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        m_MockUserRepository.Setup(x => x.GetUserAsync(m_TestUserId)).ReturnsAsync(user);
        m_MockCacheService.Setup(x => x.GetAsync<string>(It.IsAny<string>())).ReturnsAsync((string?)null);
        m_MockCacheService.Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>())).Returns(Task.CompletedTask);

        var guidCaptured = new TaskCompletionSource<string>();
        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var match = ModalGuidRegex().Match(responseData);
            if (match.Success) guidCaptured.SetResult(match.Groups[1].Value);
        });

        // Act
        var authTask = m_Service.GetAuthenticationAsync(new AuthenticationRequest(mockContext.Object, TestProfileId));
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        m_Service.NotifyAuthenticate(new AuthenticationResponse(m_TestUserId, guid, TestPassphrase,
            mockContext.Object));
        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LToken, Is.EqualTo(emptyToken));
        });
    }

    [Test]
    public async Task GetAuthenticationAsync_LongToken_SuccessfullyDecrypts()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var longToken = new string('x', 10000);
        var encryptedToken = m_EncryptionService.Encrypt(longToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        m_MockUserRepository.Setup(x => x.GetUserAsync(m_TestUserId)).ReturnsAsync(user);
        m_MockCacheService.Setup(x => x.GetAsync<string>(It.IsAny<string>())).ReturnsAsync((string?)null);
        m_MockCacheService.Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>())).Returns(Task.CompletedTask);

        var guidCaptured = new TaskCompletionSource<string>();
        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var match = ModalGuidRegex().Match(responseData);
            if (match.Success) guidCaptured.SetResult(match.Groups[1].Value);
        });

        // Act
        var authTask = m_Service.GetAuthenticationAsync(new AuthenticationRequest(mockContext.Object, TestProfileId));
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        m_Service.NotifyAuthenticate(new AuthenticationResponse(m_TestUserId, guid, TestPassphrase,
            mockContext.Object));
        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LToken, Is.EqualTo(longToken));
        });
    }

    [Test]
    public async Task GetAuthenticationAsync_UnicodeToken_SuccessfullyDecrypts()
    {
        // Arrange
        var mockContext = new Mock<IInteractionContext>();
        var interaction = m_DiscordHelper.CreateModalInteraction(m_TestUserId);
        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

        var unicodeToken = "测试数据🎮🎯🎲";
        var encryptedToken = m_EncryptionService.Encrypt(unicodeToken, TestPassphrase);

        var profile = new UserProfileDto
        {
            ProfileId = TestProfileId,
            LtUid = TestLtUid,
            LToken = encryptedToken
        };

        var user = new UserDto
        {
            Id = m_TestUserId,
            Profiles = [profile]
        };

        m_MockUserRepository.Setup(x => x.GetUserAsync(m_TestUserId)).ReturnsAsync(user);
        m_MockCacheService.Setup(x => x.GetAsync<string>(It.IsAny<string>())).ReturnsAsync((string?)null);
        m_MockCacheService.Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>())).Returns(Task.CompletedTask);

        var guidCaptured = new TaskCompletionSource<string>();
        _ = Task.Run(async () =>
        {
            var responseData = string.Empty;
            do
            {
                responseData = await m_DiscordHelper.ExtractInteractionResponseDataAsync();
                await Task.Delay(50);
            } while (string.IsNullOrEmpty(responseData));

            var match = ModalGuidRegex().Match(responseData);
            if (match.Success) guidCaptured.SetResult(match.Groups[1].Value);
        });

        // Act
        var authTask = m_Service.GetAuthenticationAsync(new AuthenticationRequest(mockContext.Object, TestProfileId));
        var guid = await guidCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        m_Service.NotifyAuthenticate(new AuthenticationResponse(m_TestUserId, guid, TestPassphrase,
            mockContext.Object));
        var result = await authTask;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LToken, Is.EqualTo(unicodeToken));
        });
    }

    #endregion

    #region Helper Methods

    [GeneratedRegex(@"auth_modal:([a-f0-9-]{36})", RegexOptions.IgnoreCase)]
    private static partial Regex ModalGuidRegex();

    #endregion
}
