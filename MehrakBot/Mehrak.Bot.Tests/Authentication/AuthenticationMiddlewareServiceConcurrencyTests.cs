#region

using System.Collections.Concurrent;
using System.Diagnostics;
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
/// Stress tests for AuthenticationMiddlewareService under high concurrency.
/// Tests the system's ability to handle multiple simultaneous authentication requests.
/// </summary>
[TestFixture]
[Explicit("Stress test - run manually to verify concurrent behavior")]
public partial class AuthenticationMiddlewareServiceConcurrencyTests
{
    private Mock<ICacheService> m_MockCacheService = null!;
    private Mock<IUserRepository> m_MockUserRepository = null!;
    private CookieEncryptionService m_EncryptionService = null!;
    private AuthenticationMiddlewareService m_Service = null!;

    private const int NumberOfConcurrentUsers = 100;
    private const int MaxParallelism = 16;
    private const uint TestProfileId = 1U;
    private const ulong BaseLtUid = 100000000UL;
    private const string CorrectPassphrase = "correct-passphrase";
    private const string WrongPassphrase = "wrong-passphrase";
    private const string TestLToken = "test-token-";
    private const double WrongPasswordProbability = 0.10; // 10% chance of wrong password

    private List<ulong> m_TestUserIds = null!;
    private ConcurrentDictionary<ulong, string> m_UserTokens = null!;
    private Random m_Random = null!;

    [SetUp]
    public void Setup()
    {
        m_MockCacheService = new Mock<ICacheService>();
        m_MockUserRepository = new Mock<IUserRepository>();
        m_EncryptionService = new CookieEncryptionService(NullLogger<CookieEncryptionService>.Instance);
        m_Random = new Random(42); // Fixed seed for reproducibility

        m_Service = new AuthenticationMiddlewareService(
            m_MockCacheService.Object,
            m_EncryptionService,
            m_MockUserRepository.Object,
            NullLogger<AuthenticationMiddlewareService>.Instance);

        // Generate unique test user IDs
        m_TestUserIds = new List<ulong>(NumberOfConcurrentUsers);
        m_UserTokens = new ConcurrentDictionary<ulong, string>();

        var baseUserId = (ulong)(DateTime.UtcNow.Ticks % 1_000_000_000) * 1000;
        for (int i = 0; i < NumberOfConcurrentUsers; i++)
        {
            var userId = baseUserId + (ulong)i;
            m_TestUserIds.Add(userId);
            m_UserTokens[userId] = $"{TestLToken}{userId}";
        }

        // Setup cache to always return null (force authentication flow)
        m_MockCacheService
            .Setup(x => x.GetAsync<string>(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        m_MockCacheService
            .Setup(x => x.SetAsync(It.IsAny<ICacheEntry<string>>()))
            .Returns(Task.CompletedTask);

        // Setup user repository to return users with encrypted tokens
        m_MockUserRepository
            .Setup(x => x.GetUserAsync(It.IsAny<ulong>()))
            .ReturnsAsync((ulong userId) =>
            {
                if (!m_UserTokens.ContainsKey(userId))
                    return null;

                var token = m_UserTokens[userId];
                var encryptedToken = m_EncryptionService.Encrypt(token, CorrectPassphrase);

                var profile = new UserProfile
                {
                    ProfileId = TestProfileId,
                    LtUid = BaseLtUid + userId % 1000,
                    LToken = encryptedToken
                };

                return new UserModel
                {
                    Id = userId,
                    Profiles = new List<UserProfile> { profile }
                };
            });
    }

    [TearDown]
    public void TearDown()
    {
        m_MockCacheService.Reset();
        m_MockUserRepository.Reset();
    }

    [Test]
    public async Task HighConcurrency_MultipleUsers_AllRequestsHandledCorrectly()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();
        var successCount = 0;
        var failureCount = 0;
        var timeoutCount = 0;
        var wrongPasswordCount = 0;
        var exceptionCount = 0;

        var successLock = new object();
        var failureLock = new object();
        var timeoutLock = new object();
        var wrongPasswordLock = new object();
        var exceptionLock = new object();

        await TestContext.Out.WriteLineAsync($"Starting high concurrency test with {NumberOfConcurrentUsers} users");
        await TestContext.Out.WriteLineAsync($"Max parallelism: {MaxParallelism}");
        await TestContext.Out.WriteLineAsync($"Wrong password probability: {WrongPasswordProbability:P0}");
        await TestContext.Out.WriteLineAsync("---");

        // Act - Process all users concurrently
        await Parallel.ForEachAsync(m_TestUserIds,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism },
            async (userId, cancellationToken) =>
            {
                DiscordTestHelper? helper = null;
                try
                {
                    // Each user gets their own DiscordTestHelper instance
                    helper = new DiscordTestHelper();
                    helper.SetupRequestCapture();

                    var mockContext = new Mock<IInteractionContext>();
                    var interaction = helper.CreateModalInteraction(userId);
                    mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

                    var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

                    // Start authentication
                    var authTask = m_Service.GetAuthenticationAsync(request);

                    // Extract GUID from modal response
                    var guid = await ExtractGuidWithRetryAsync(helper, cancellationToken);

                    if (guid == null)
                    {
                        lock (timeoutLock)
                        {
                            timeoutCount++;
                        }

                        await TestContext.Out.WriteLineAsync($"User {userId}: Failed to extract GUID");
                        return;
                    }

                    // Determine if this user will provide wrong password (10% chance)
                    var useWrongPassword = m_Random.NextDouble() < WrongPasswordProbability;
                    var passphrase = useWrongPassword ? WrongPassphrase : CorrectPassphrase;

                    if (useWrongPassword)
                        lock (wrongPasswordLock)
                        {
                            wrongPasswordCount++;
                        }

                    // Notify with passphrase
                    var authResponse = new AuthenticationResponse(
                        userId,
                        guid,
                        passphrase,
                        mockContext.Object);

                    var notifyResult = m_Service.NotifyAuthenticate(authResponse);

                    if (!notifyResult)
                    {
                        lock (failureLock)
                        {
                            failureCount++;
                        }

                        await TestContext.Out.WriteLineAsync($"User {userId}: Notify failed (GUID not found)");
                        return;
                    }

                    // Wait for authentication result
                    var result = await authTask;

                    // Track results
                    if (result.Status == AuthStatus.Success)
                    {
                        lock (successLock)
                        {
                            successCount++;
                        }

                        // Verify token matches expected value
                        var expectedToken = m_UserTokens[userId];
                        if (result.LToken != expectedToken)
                            await TestContext.Out.WriteLineAsync(
                                $"User {userId}: Token mismatch! Expected: {expectedToken}, Got: {result.LToken}");
                    }
                    else if (result.Status == AuthStatus.Failure)
                    {
                        lock (failureLock)
                        {
                            failureCount++;
                        }

                        // Verify failure reason
                        if (useWrongPassword && !result.ErrorMessage!.Contains("Incorrect passphrase"))
                            await TestContext.Out.WriteLineAsync(
                                $"User {userId}: Expected 'Incorrect passphrase' error, got: {result.ErrorMessage}");
                    }
                    else if (result.Status == AuthStatus.Timeout)
                    {
                        lock (timeoutLock)
                        {
                            timeoutCount++;
                        }

                        await TestContext.Out.WriteLineAsync($"User {userId}: Authentication timed out");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptionLock)
                    {
                        exceptionCount++;
                    }

                    await TestContext.Out.WriteLineAsync(
                        $"User {userId}: Exception - {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    helper?.Dispose();
                }
            });

        stopwatch.Stop();

        // Assert
        await TestContext.Out.WriteLineAsync("---");
        await TestContext.Out.WriteLineAsync("Test Results:");
        await TestContext.Out.WriteLineAsync($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        await TestContext.Out.WriteLineAsync(
            $"Average time per user: {stopwatch.Elapsed.TotalMilliseconds / NumberOfConcurrentUsers:F2}ms");
        await TestContext.Out.WriteLineAsync($"Total users: {NumberOfConcurrentUsers}");
        await TestContext.Out.WriteLineAsync($"Successful authentications: {successCount}");
        await TestContext.Out.WriteLineAsync($"Failed authentications (wrong password): {failureCount}");
        await TestContext.Out.WriteLineAsync($"Timeouts: {timeoutCount}");
        await TestContext.Out.WriteLineAsync($"Exceptions: {exceptionCount}");
        await TestContext.Out.WriteLineAsync($"Expected wrong passwords: {wrongPasswordCount}");
        await TestContext.Out.WriteLineAsync("---");

        // Verify results
        Assert.Multiple(() =>
        {
            // All users should complete (success + failure = total)
            var totalCompleted = successCount + failureCount;
            Assert.That(totalCompleted, Is.EqualTo(NumberOfConcurrentUsers),
                $"Expected all {NumberOfConcurrentUsers} users to complete authentication");

            // No timeouts should occur
            Assert.That(timeoutCount, Is.EqualTo(0),
                "No authentication should timeout in normal conditions");

            // No exceptions should occur
            Assert.That(exceptionCount, Is.EqualTo(0),
                "No exceptions should occur during concurrent authentication");

            // Success count should match users with correct password
            var expectedSuccessCount = NumberOfConcurrentUsers - wrongPasswordCount;
            Assert.That(successCount, Is.EqualTo(expectedSuccessCount),
                $"Expected {expectedSuccessCount} successful authentications (users with correct password)");

            // Failure count should match users with wrong password
            Assert.That(failureCount, Is.EqualTo(wrongPasswordCount),
                $"Expected {wrongPasswordCount} failed authentications (users with wrong password)");

            // Test should complete in reasonable time (less than 30 seconds for 100 users)
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(30),
                "Test should complete within reasonable time");
        });

        // Verify caching behavior
        m_MockCacheService.Verify(
            x => x.SetAsync(It.IsAny<ICacheEntry<string>>()),
            Times.Exactly(successCount),
            "Only successful authentications should cache tokens");
    }

    [Test]
    public async Task HighConcurrency_SameUser_MultipleSimultaneousRequests_OnlyOneSucceeds()
    {
        // Arrange
        const int simultaneousRequests = 20;
        var userId = m_TestUserIds[0];
        var stopwatch = Stopwatch.StartNew();

        await TestContext.Out.WriteLineAsync($"Testing {simultaneousRequests} simultaneous requests for user {userId}");

        var tasks = new List<Task<(bool NotifySuccess, AuthenticationResult Result, string Guid)>>();
        var helpers = new List<DiscordTestHelper>();

        // Act - Create multiple simultaneous authentication requests for same user
        for (int i = 0; i < simultaneousRequests; i++)
        {
            var helper = new DiscordTestHelper();
            helper.SetupRequestCapture();
            helpers.Add(helper);

            var task = Task.Run(async () =>
            {
                var mockContext = new Mock<IInteractionContext>();
                var interaction = helper.CreateModalInteraction(userId);
                mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

                var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

                // Start authentication
                var authTask = m_Service.GetAuthenticationAsync(request);

                // Extract GUID
                var guid = await ExtractGuidWithRetryAsync(helper, CancellationToken.None);

                if (guid == null)
                    return (false, AuthenticationResult.Timeout(), string.Empty);

                // All requests use correct passphrase
                var authResponse = new AuthenticationResponse(userId, guid, CorrectPassphrase, mockContext.Object);
                var notifyResult = m_Service.NotifyAuthenticate(authResponse);

                var result = await authTask;
                return (notifyResult, result, guid);
            });

            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Cleanup
        foreach (var helper in helpers) helper.Dispose();

        // Assert
        var successfulNotifications = results.Count(r => r.NotifySuccess);
        var successfulAuthentications = results.Count(r => r.Result.IsSuccess);
        var uniqueGuids = results.Where(r => !string.IsNullOrEmpty(r.Guid)).Select(r => r.Guid).Distinct().Count();

        await TestContext.Out.WriteLineAsync("---");
        await TestContext.Out.WriteLineAsync($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        await TestContext.Out.WriteLineAsync($"Unique GUIDs: {uniqueGuids}");
        await TestContext.Out.WriteLineAsync($"Successful notifications: {successfulNotifications}");
        await TestContext.Out.WriteLineAsync($"Successful authentications: {successfulAuthentications}");
        await TestContext.Out.WriteLineAsync("---");

        Assert.Multiple(() =>
        {
            // All requests should get unique GUIDs
            Assert.That(uniqueGuids, Is.EqualTo(simultaneousRequests),
                "Each authentication request should get a unique GUID");

            // All notifications should succeed (each GUID is valid)
            Assert.That(successfulNotifications, Is.EqualTo(simultaneousRequests),
                "All notifications should succeed since each has a unique GUID");

            // All authentications should succeed
            Assert.That(successfulAuthentications, Is.EqualTo(simultaneousRequests),
                "All authentications should succeed with correct passphrase");
        });
    }

    [Test]
    public async Task HighConcurrency_RapidSuccessiveRequests_NoDataCorruption()
    {
        // Arrange
        const int requestsPerUser = 5;
        const int numberOfUsers = 20;
        var userIds = m_TestUserIds.Take(numberOfUsers).ToList();

        var stopwatch = Stopwatch.StartNew();
        var allResults = new ConcurrentBag<(ulong UserId, int RequestNumber, bool Success, string? Token)>();

        await TestContext.Out.WriteLineAsync(
            $"Testing {requestsPerUser} successive requests for each of {numberOfUsers} users");

        // Act - Each user makes multiple requests in rapid succession
        await Parallel.ForEachAsync(userIds,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism },
            async (userId, cancellationToken) =>
            {
                for (int requestNum = 0; requestNum < requestsPerUser; requestNum++)
                {
                    using var helper = new DiscordTestHelper();
                    helper.SetupRequestCapture();

                    try
                    {
                        var mockContext = new Mock<IInteractionContext>();
                        var interaction = helper.CreateModalInteraction(userId);
                        mockContext.SetupGet(x => x.Interaction).Returns(() => interaction);

                        var request = new AuthenticationRequest(mockContext.Object, TestProfileId);

                        var authTask = m_Service.GetAuthenticationAsync(request);
                        var guid = await ExtractGuidWithRetryAsync(helper, cancellationToken);

                        if (guid == null)
                        {
                            allResults.Add((userId, requestNum, false, null));
                            continue;
                        }

                        var authResponse =
                            new AuthenticationResponse(userId, guid, CorrectPassphrase, mockContext.Object);
                        m_Service.NotifyAuthenticate(authResponse);

                        var result = await authTask;

                        allResults.Add((userId, requestNum, result.IsSuccess, result.LToken));
                    }
                    catch
                    {
                        allResults.Add((userId, requestNum, false, null));
                    }
                }
            });

        stopwatch.Stop();

        // Assert
        var totalRequests = numberOfUsers * requestsPerUser;
        var successfulRequests = allResults.Count(r => r.Success);
        var groupedByUser = allResults.GroupBy(r => r.UserId);

        await TestContext.Out.WriteLineAsync("---");
        await TestContext.Out.WriteLineAsync($"Total time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        await TestContext.Out.WriteLineAsync($"Total requests: {totalRequests}");
        await TestContext.Out.WriteLineAsync($"Successful requests: {successfulRequests}");
        await TestContext.Out.WriteLineAsync($"Success rate: {(double)successfulRequests / totalRequests:P2}");
        await TestContext.Out.WriteLineAsync("---");

        Assert.Multiple(() =>
        {
            // All requests should complete
            Assert.That(allResults.Count, Is.EqualTo(totalRequests),
                "All requests should complete");

            // Most requests should succeed (allowing for some race conditions)
            Assert.That(successfulRequests, Is.GreaterThanOrEqualTo((int)(totalRequests * 0.95)),
                "At least 95% of requests should succeed");

            // Verify no token corruption - all successful requests for same user should get same token
            foreach (var userGroup in groupedByUser)
            {
                var successfulTokens = userGroup.Where(r => r.Success && r.Token != null).Select(r => r.Token)
                    .Distinct().ToList();
                var expectedToken = m_UserTokens[userGroup.Key];

                Assert.That(successfulTokens, Has.All.EqualTo(expectedToken),
                    $"User {userGroup.Key} should always get their correct token");
            }
        });
    }

    #region Helper Methods

    private static async Task<string?> ExtractGuidWithRetryAsync(DiscordTestHelper helper,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 50; // 50 * 100ms = 5 seconds max wait
        const int delayMs = 100;

        for (int i = 0; i < maxRetries; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var responseData = await helper.ExtractInteractionResponseDataAsync();

            if (!string.IsNullOrEmpty(responseData))
            {
                var guidMatch = ModalGuidRegex().Match(responseData);
                if (guidMatch.Success) return guidMatch.Groups[1].Value;
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        return null;
    }

    [GeneratedRegex(@"auth_modal:([a-f0-9-]{36})", RegexOptions.IgnoreCase)]
    private static partial Regex ModalGuidRegex();

    #endregion
}
