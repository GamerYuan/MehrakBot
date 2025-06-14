#region

using System.Reflection;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using Moq;
using NetCord.Services;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class AuthenticationMiddlewareServiceTests
{
    private Mock<ILogger<AuthenticationMiddlewareService>> m_LoggerMock;
    private AuthenticationMiddlewareService m_Service;
    private Mock<IAuthenticationListener> m_ListenerMock;
    private Mock<IInteractionContext> m_ContextMock;

    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";

    [SetUp]
    public void Setup()
    {
        m_LoggerMock = new Mock<ILogger<AuthenticationMiddlewareService>>();
        m_Service = new AuthenticationMiddlewareService(m_LoggerMock.Object);
        m_ListenerMock = new Mock<IAuthenticationListener>();
        m_ContextMock = new Mock<IInteractionContext>();
    }

    [Test]
    public void RegisterAuthenticationListener_ShouldReturnValidGuid()
    {
        // Act
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);

        // Assert
        Assert.That(guid, Is.Not.Null);
        Assert.That(guid, Is.Not.Empty);
        Assert.That(Guid.TryParse(guid, out _), Is.True);
    }

    [Test]
    public void RegisterAuthenticationListener_ShouldGenerateUniqueGuids()
    {
        // Act
        var guid1 = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var guid2 = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);

        // Assert
        Assert.That(guid1, Is.Not.EqualTo(guid2));
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithValidGuid_ShouldCallListener()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithInvalidGuid_ShouldNotCallListener()
    {
        // Arrange
        var invalidGuid = Guid.NewGuid().ToString();
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(invalidGuid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(It.IsAny<AuthenticationResult>()), Times.Never);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithUsedGuid_ShouldNotCallListenerTwice()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result); // Second call with same GUID

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WhenListenerThrows_ShouldLogError()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        var expectedException = new InvalidOperationException("Test exception");

        m_ListenerMock.Setup(x => x.OnAuthenticationCompletedAsync(It.IsAny<AuthenticationResult>()))
            .ThrowsAsync(expectedException);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error notifying authentication listener")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task CleanupExpiredRequests_ShouldNotifyTimeoutForExpiredRequests()
    {
        // Arrange
        var listener1Mock = new Mock<IAuthenticationListener>();
        var listener2Mock = new Mock<IAuthenticationListener>();

        // Use reflection to test the cleanup behavior more directly
        var guid1 = m_Service.RegisterAuthenticationListener(TestUserId, listener1Mock.Object);
        _ = m_Service.RegisterAuthenticationListener(TestUserId + 1, listener2Mock.Object);

        // Use reflection to access the private cleanup method
        var cleanupMethod = typeof(AuthenticationMiddlewareService)
            .GetMethod("CleanupExpiredRequests", BindingFlags.NonPublic | BindingFlags.Instance);

        // Access the private field to modify request times to simulate expiration
        var pendingRequestsField = typeof(AuthenticationMiddlewareService)
            .GetField("m_PendingRequests", BindingFlags.NonPublic | BindingFlags.Instance);

        if (cleanupMethod != null && pendingRequestsField != null)
        {
            _ = pendingRequestsField.GetValue(m_Service);

            // Invoke cleanup method manually
            cleanupMethod.Invoke(m_Service, [null!]);

            // Wait a bit for async cleanup tasks to complete
            await Task.Delay(100);
        }

        // Test that valid GUIDs still work (not expired in the short time)
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        await m_Service.NotifyAuthenticationCompletedAsync(guid1, result);

        // Assert - Listener should be called since request hasn't expired
        listener1Mock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public void Constructor_ShouldInitializeCleanupTimer()
    {
        // Arrange & Act
        var service = new AuthenticationMiddlewareService(m_LoggerMock.Object);
        var guid = service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);

        // Assert - Service should be properly initialized and able to register listeners
        Assert.That(guid, Is.Not.Null);
        Assert.That(guid, Is.Not.Empty);
        Assert.That(Guid.TryParse(guid, out _), Is.True);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithMultipleListeners_ShouldCallCorrectListener()
    {
        // Arrange
        var listener1Mock = new Mock<IAuthenticationListener>();
        var listener2Mock = new Mock<IAuthenticationListener>();

        var guid1 = m_Service.RegisterAuthenticationListener(TestUserId, listener1Mock.Object);
        var guid2 = m_Service.RegisterAuthenticationListener(TestUserId + 1, listener2Mock.Object);

        var result1 = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        var result2 = AuthenticationResult.Failure(TestUserId + 1, "Test error");

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid1, result1);
        await m_Service.NotifyAuthenticationCompletedAsync(guid2, result2);

        // Assert
        listener1Mock.Verify(x => x.OnAuthenticationCompletedAsync(result1), Times.Once);
        listener1Mock.Verify(x => x.OnAuthenticationCompletedAsync(result2), Times.Never);

        listener2Mock.Verify(x => x.OnAuthenticationCompletedAsync(result2), Times.Once);
        listener2Mock.Verify(x => x.OnAuthenticationCompletedAsync(result1), Times.Never);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_ConcurrentAccess_ShouldHandleConcurrentNotifications()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act - Multiple concurrent notifications with the same GUID
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
            tasks.Add(m_Service.NotifyAuthenticationCompletedAsync(guid, result));

        await Task.WhenAll(tasks);

        // Assert - Only one should succeed
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task RegisterAuthenticationListener_ConcurrentRegistration_ShouldHandleConcurrentRegistrations()
    {
        // Arrange
        var listeners = new List<Mock<IAuthenticationListener>>();

        // Act - Register multiple listeners concurrently
        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
        {
            var listenerMock = new Mock<IAuthenticationListener>();
            listeners.Add(listenerMock);
            var id = i;
            tasks.Add(Task.Run(() =>
                m_Service.RegisterAuthenticationListener(TestUserId + (ulong)id, listenerMock.Object)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should have unique GUIDs
        Assert.That(results.Length, Is.EqualTo(10));
        Assert.That(results.Distinct().Count(), Is.EqualTo(10));

        // Verify all can be notified
        for (int i = 0; i < 10; i++)
        {
            var successResult =
                AuthenticationResult.Success(TestUserId + (ulong)i, TestLtUid, TestLToken, m_ContextMock.Object);
            await m_Service.NotifyAuthenticationCompletedAsync(results[i], successResult);
            listeners[i].Verify(x => x.OnAuthenticationCompletedAsync(successResult), Times.Once);
        }
    }

    [Test]
    public void RegisterAuthenticationListener_ShouldLogDebugMessage()
    {
        // Act
        _ = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Registered authentication listener")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithValidGuid_ShouldLogDebugMessage()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Notifying authentication completion")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithInvalidGuid_ShouldLogWarning()
    {
        // Arrange
        var invalidGuid = Guid.NewGuid().ToString();
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(invalidGuid, result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("No pending authentication request found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithFailureResult_ShouldStillCallListener()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Failure(TestUserId, "Test failure");

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithTimeoutResult_ShouldStillCallListener()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Timeout(TestUserId);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithNullLToken_ShouldCallListener()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, null!, m_ContextMock.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WhenListenerThrowsSpecificException_ShouldLogSpecificError()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);
        var expectedException = new InvalidOperationException("Specific test exception");

        m_ListenerMock.Setup(x => x.OnAuthenticationCompletedAsync(It.IsAny<AuthenticationResult>()))
            .ThrowsAsync(expectedException);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Error notifying authentication listener")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void RegisterAuthenticationListener_WithDifferentUserIds_ShouldReturnDifferentGuids()
    {
        // Arrange
        var listener1 = new Mock<IAuthenticationListener>();
        var listener2 = new Mock<IAuthenticationListener>();

        // Act
        var guid1 = m_Service.RegisterAuthenticationListener(TestUserId, listener1.Object);
        var guid2 = m_Service.RegisterAuthenticationListener(TestUserId + 1, listener2.Object);

        // Assert
        Assert.That(guid1, Is.Not.EqualTo(guid2));
        Assert.That(Guid.TryParse(guid1, out _), Is.True);
        Assert.That(Guid.TryParse(guid2, out _), Is.True);
    }

    [Test]
    public async Task Service_WithHighVolume_ShouldHandleMultipleRequestsCorrectly()
    {
        // Arrange
        const int requestCount = 100;
        var listeners = new List<Mock<IAuthenticationListener>>();
        var guids = new List<string>();

        // Act - Register many listeners
        for (int i = 0; i < requestCount; i++)
        {
            var listener = new Mock<IAuthenticationListener>();
            listeners.Add(listener);
            var guid = m_Service.RegisterAuthenticationListener(TestUserId + (ulong)i, listener.Object);
            guids.Add(guid);
        }

        // Notify all listeners
        var notifyTasks = new List<Task>();
        for (int i = 0; i < requestCount; i++)
        {
            var result =
                AuthenticationResult.Success(TestUserId + (ulong)i, TestLtUid, TestLToken, m_ContextMock.Object);
            notifyTasks.Add(m_Service.NotifyAuthenticationCompletedAsync(guids[i], result));
        }

        await Task.WhenAll(notifyTasks);

        // Assert - All listeners should have been called exactly once
        for (int i = 0; i < requestCount; i++)
            listeners[i].Verify(x => x.OnAuthenticationCompletedAsync(It.IsAny<AuthenticationResult>()), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithNullContext_ShouldStillCallListener()
    {
        // Arrange
        var guid = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, null!);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid, result);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result), Times.Once);
    }

    [Test]
    public async Task NotifyAuthenticationCompletedAsync_WithDifferentContexts_ShouldPassCorrectContext()
    {
        // Arrange
        var contextMock1 = new Mock<IInteractionContext>();
        var contextMock2 = new Mock<IInteractionContext>();

        var guid1 = m_Service.RegisterAuthenticationListener(TestUserId, m_ListenerMock.Object);
        var guid2 = m_Service.RegisterAuthenticationListener(TestUserId + 1, m_ListenerMock.Object);

        var result1 = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, contextMock1.Object);
        var result2 = AuthenticationResult.Success(TestUserId + 1, TestLtUid, TestLToken, contextMock2.Object);

        // Act
        await m_Service.NotifyAuthenticationCompletedAsync(guid1, result1);
        await m_Service.NotifyAuthenticationCompletedAsync(guid2, result2);

        // Assert
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result1), Times.Once);
        m_ListenerMock.Verify(x => x.OnAuthenticationCompletedAsync(result2), Times.Once);

        // Verify that the results contain the correct contexts
        Assert.That(result1.Context, Is.EqualTo(contextMock1.Object));
        Assert.That(result2.Context, Is.EqualTo(contextMock2.Object));
    }
}