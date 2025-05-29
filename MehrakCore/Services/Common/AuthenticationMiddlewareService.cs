#region

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public interface IAuthenticationListener
{
    Task OnAuthenticationCompletedAsync(AuthenticationResult result);
}

public class AuthenticationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public ulong UserId { get; private init; }
    public ulong LtUid { get; private init; }
    public string? LToken { get; private init; }

    public static AuthenticationResult Success(ulong userId, ulong ltUid, string ltoken)
    {
        return new AuthenticationResult { IsSuccess = true, UserId = userId, LtUid = ltUid, LToken = ltoken };
    }

    public static AuthenticationResult Failure(ulong userId, string errorMessage)
    {
        return new AuthenticationResult { IsSuccess = false, UserId = userId, ErrorMessage = errorMessage };
    }

    public static AuthenticationResult Timeout(ulong userId)
    {
        return new AuthenticationResult
        { IsSuccess = false, UserId = userId, ErrorMessage = "Authentication timed out" };
    }
}

public class AuthenticationMiddlewareService : IAuthenticationMiddlewareService
{
    private readonly ILogger<AuthenticationMiddlewareService> m_Logger;
    private readonly ConcurrentDictionary<string, AuthenticationRequest> m_PendingRequests;

    private const int TimeoutMinutes = 5;

    public AuthenticationMiddlewareService(ILogger<AuthenticationMiddlewareService> logger)
    {
        m_Logger = logger;
        m_PendingRequests = new ConcurrentDictionary<string, AuthenticationRequest>();

        // Cleanup expired requests every minute
        _ = new Timer(CleanupExpiredRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public string RegisterAuthenticationListener(ulong userId, IAuthenticationListener listener)
    {
        var guid = Guid.CreateVersion7().ToString();

        var request = new AuthenticationRequest
        {
            Id = guid,
            UserId = userId,
            Listener = listener,
            RequestTime = DateTime.UtcNow
        };

        m_PendingRequests[guid] = request;
        m_Logger.LogDebug("Registered authentication listener for user {UserId} with guid {Guid}",
            userId, guid);

        return guid;
    }

    public async Task NotifyAuthenticationCompletedAsync(string guid, AuthenticationResult result)
    {
        if (m_PendingRequests.TryRemove(guid, out var request))
        {
            m_Logger.LogDebug(
                "Notifying authentication completion for user {UserId} with guid {Guid}, success: {IsSuccess}",
                result.UserId, guid, result.IsSuccess);

            try
            {
                await request.Listener.OnAuthenticationCompletedAsync(result);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Error notifying authentication listener for user {UserId}", result.UserId);
            }
        }
        else
        {
            m_Logger.LogWarning("No pending authentication request found for guid {Guid}", guid);
        }
    }

    private void CleanupExpiredRequests(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-TimeoutMinutes);
        var expiredRequests = m_PendingRequests
            .Where(kvp => kvp.Value.RequestTime < cutoff)
            .ToList();

        foreach (var (messageId, request) in expiredRequests)
            if (m_PendingRequests.TryRemove(messageId, out _))
            {
                m_Logger.LogDebug("Authentication request timed out for user {UserId} with message ID {MessageId}",
                    request.UserId, messageId);

                var timeoutResult = AuthenticationResult.Timeout(request.UserId);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await request.Listener.OnAuthenticationCompletedAsync(timeoutResult);
                    }
                    catch (Exception ex)
                    {
                        m_Logger.LogError(ex, "Error notifying timeout to authentication listener for user {UserId}",
                            request.UserId);
                    }
                });
            }
    }

    private class AuthenticationRequest
    {
        public required string Id { get; init; }
        public ulong UserId { get; init; }
        public IAuthenticationListener Listener { get; init; } = null!;
        public DateTime RequestTime { get; init; }
    }
}
