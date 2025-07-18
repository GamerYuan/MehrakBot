#region

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Common;

public interface IAuthenticationListener
{
    Task OnAuthenticationCompletedAsync(AuthenticationResult result);
}

public class AuthenticationResult
{
    [MemberNotNullWhen(true, nameof(LToken))]
    [MemberNotNullWhen(true, nameof(Context))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess { get; private init; }

    public string? ErrorMessage { get; private init; }
    public ulong UserId { get; private init; }
    public ulong LtUid { get; private init; }
    public string? LToken { get; private init; }
    public IInteractionContext? Context { get; private init; }

    public static AuthenticationResult Success(ulong userId, ulong ltUid, string ltoken, IInteractionContext context)
    {
        return new AuthenticationResult
            { IsSuccess = true, UserId = userId, LtUid = ltUid, LToken = ltoken, Context = context };
    }

    public static AuthenticationResult Failure(ulong userId, string errorMessage)
    {
        return new AuthenticationResult
            { IsSuccess = false, UserId = userId, ErrorMessage = errorMessage };
    }

    public static AuthenticationResult Timeout(ulong userId)
    {
        return new AuthenticationResult
            { IsSuccess = false, UserId = userId, ErrorMessage = "Authentication timed out" };
    }
}

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

        // Cleanup expired requests every minute
        m_Timer = new Timer(CleanupExpiredRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        m_Timer.Dispose();
    }

    public string RegisterAuthenticationListener(ulong userId, IAuthenticationListener listener)
    {
        var guid = Guid.CreateVersion7().ToString();

        var request = new AuthenticationRequest
        {
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

    public bool ContainsAuthenticationRequest(string guid)
    {
        return m_PendingRequests.ContainsKey(guid);
    }

    private void CleanupExpiredRequests(object? state)
    {
        m_Logger.LogDebug("Cleaning up expired authentication requests");
        var cutoff = DateTime.UtcNow.AddMinutes(-TimeoutMinutes);
        var expiredRequests = m_PendingRequests
            .Where(kvp => kvp.Value.RequestTime < cutoff)
            .ToList();

        foreach (var (messageId, request) in expiredRequests)
            if (m_PendingRequests.TryRemove(messageId, out _))
                m_Logger.LogDebug("Authentication request timed out for user {UserId} with message ID {MessageId}",
                    request.UserId, messageId);
    }

    private class AuthenticationRequest
    {
        public ulong UserId { get; init; }
        public IAuthenticationListener Listener { get; init; } = null!;
        public DateTime RequestTime { get; init; }
    }
}
