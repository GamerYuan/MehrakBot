using System.Diagnostics.CodeAnalysis;
using NetCord.Services;

namespace Mehrak.Bot.Authentication;

public interface IAuthenticationMiddlewareService
{
    string RegisterAuthenticationListener(ulong userId, IAuthenticationListener listener);
    Task NotifyAuthenticationCompletedAsync(string guid, AuthenticationResult result);
    bool ContainsAuthenticationRequest(string guid);
}

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

    public static AuthenticationResult Success(
        ulong userId,
        ulong ltUid,
        string ltoken,
        IInteractionContext context)
    {
        return new AuthenticationResult
        {
            IsSuccess = true,
            UserId = userId,
            LtUid = ltUid,
            LToken = ltoken,
            Context = context
        };
    }

    public static AuthenticationResult Failure(ulong userId, string errorMessage)
    {
        return new AuthenticationResult
        {
            IsSuccess = false,
            UserId = userId,
            ErrorMessage = errorMessage
        };
    }

    public static AuthenticationResult Timeout(ulong userId)
    {
        return new AuthenticationResult
        {
            IsSuccess = false,
            UserId = userId,
            ErrorMessage = "Authentication timed out"
        };
    }
}
