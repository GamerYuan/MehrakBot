#region

#endregion

namespace MehrakCore.Services.Common;

public interface IAuthenticationMiddlewareService
{
    string RegisterAuthenticationListener(ulong userId, IAuthenticationListener listener);
    Task NotifyAuthenticationCompletedAsync(string guid, AuthenticationResult result);
}
