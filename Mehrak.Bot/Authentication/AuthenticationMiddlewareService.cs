using Mehrak.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Mehrak.Bot.Authentication;

public class AuthenticationMiddlewareService : IAuthenticationMiddlewareService, IDisposable
{
    private readonly ILogger<AuthenticationMiddlewareService> m_Logger;
    private readonly ConcurrentDictionary<string, AuthenticationRequest> m_PendingRequests;
    private readonly Timer m_Timer;

    private const int TimeoutMinutes = 1;

    public AuthenticationMiddlewareService(ILogger<AuthenticationMiddlewareService> logger)
    {
        m_Logger = logger;
        m_PendingRequests = new ConcurrentDictionary<string, AuthenticationRequest>();

        m_Timer = new Timer(CleanupExpiredRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_Timer.Dispose();
        }
    }

    public string RegisterAuthenticationListener(ulong userId, IAuthenticationListener listener)
    {
        string guid = Guid.CreateVersion7().ToString();

        AuthenticationRequest request = new()
        {
            UserId = userId,
            Listener = listener,
            RequestTime = DateTime.UtcNow
        };

        m_PendingRequests[guid] = request;
        m_Logger.LogDebug(
            "Registered authentication listener for user {UserId} with guid {Guid}",
            userId,
            guid);

        return guid;
    }

    public async Task NotifyAuthenticationCompletedAsync(string guid, AuthenticationResult result)
    {
        if (m_PendingRequests.TryRemove(guid, out AuthenticationRequest? request))
        {
            m_Logger.LogDebug(
                "Notifying authentication completion for user {UserId} with guid {Guid}, success: {IsSuccess}",
                result.UserId,
                guid,
                result.IsSuccess);

            try
            {
                await request.Listener.OnAuthenticationCompletedAsync(result);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(
                    ex,
                    "Error notifying authentication listener for user {UserId}",
                    result.UserId);
            }
        }
        else
        {
            m_Logger.LogWarning("No pending authentication request found for guid {Guid}", guid);
        }
    }

    public bool ContainsAuthenticationRequest(string guid)
    {
        return m_PendingRequests.ContainsKey(guid);
    }

    private void CleanupExpiredRequests(object? state)
    {
        m_Logger.LogDebug("Cleaning up expired authentication requests");
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-TimeoutMinutes);
        List<KeyValuePair<string, AuthenticationRequest>> expiredRequests = m_PendingRequests
            .Where(kvp => kvp.Value.RequestTime < cutoff)
            .ToList();

        foreach ((string? messageId, AuthenticationRequest? request) in expiredRequests)
        {
            if (m_PendingRequests.TryRemove(messageId, out _))
            {
                m_Logger.LogDebug(
                    "Authentication request timed out for user {UserId} with message ID {MessageId}",
                    request.UserId,
                    messageId);
            }
        }
    }

    private sealed class AuthenticationRequest
    {
        public ulong UserId { get; init; }
        public IAuthenticationListener Listener { get; init; } = null!;
        public DateTime RequestTime { get; init; }
    }
}
