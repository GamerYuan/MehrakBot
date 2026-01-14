#region

using System.Diagnostics.CodeAnalysis;
using Mehrak.Domain.Models;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Authentication;

public interface IAuthenticationMiddlewareService
{
    Task<AuthenticationResult> GetAuthenticationAsync(AuthenticationRequest request);

    bool NotifyAuthenticate(AuthenticationResponse request);
}

public class AuthenticationResult
{
    public AuthStatus Status { get; private init; }

    [MemberNotNullWhen(true, nameof(LToken))]
    [MemberNotNullWhen(true, nameof(User))]
    [MemberNotNullWhen(true, nameof(Context))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess => Status == AuthStatus.Success;

    public string? ErrorMessage { get; private init; }
    public ulong UserId { get; private init; }
    public ulong LtUid { get; private init; }
    public string? LToken { get; private init; }
    public UserDto? User { get; private init; }
    public IInteractionContext? Context { get; private init; }

    public static AuthenticationResult Success(
        ulong userId,
        ulong ltuid,
        string ltoken,
        UserDto user,
        IInteractionContext context)
    {
        return new AuthenticationResult
        {
            Status = AuthStatus.Success,
            UserId = userId,
            LtUid = ltuid,
            LToken = ltoken,
            User = user,
            Context = context
        };
    }

    public static AuthenticationResult Failure(IInteractionContext newContext, string errorMessage)
    {
        return new AuthenticationResult
        {
            Status = AuthStatus.Failure,
            Context = newContext,
            ErrorMessage = errorMessage
        };
    }

    public static AuthenticationResult NotFound(IInteractionContext context, string errorMessage)
    {
        return new AuthenticationResult
        {
            Status = AuthStatus.NotFound,
            Context = context,
            ErrorMessage = errorMessage
        };
    }

    public static AuthenticationResult Timeout()
    {
        return new AuthenticationResult
        {
            Status = AuthStatus.Timeout,
            ErrorMessage = "Authentication timed out"
        };
    }
}

public class AuthenticationRequest
{
    public IInteractionContext Context { get; init; }
    public int ProfileId { get; init; }

    public AuthenticationRequest(IInteractionContext context, int profileId)
    {
        Context = context;
        ProfileId = profileId;
    }
}

public class AuthenticationResponse
{
    public ulong UserId { get; init; }
    public string Guid { get; init; }
    public string Passphrase { get; init; }
    public IInteractionContext Context { get; init; }

    public AuthenticationResponse(ulong userId, string guid, string passphrase, IInteractionContext context)
    {
        UserId = userId;
        Guid = guid;
        Passphrase = passphrase;
        Context = context;
    }
}

public enum AuthStatus
{
    Success,
    Failure,
    Timeout,
    NotFound
}
