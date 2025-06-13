#region

using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands.Hsr.RealTimeNotes;

public class HsrRealTimeNotesCommandExecutor : IRealTimeNotesCommandExecutor<HsrCommandModule>, IAuthenticationListener
{
    private readonly IRealTimeNotesApiService<HsrRealTimeNotesData> m_ApiService;
    public IInteractionContext Context { get; set; } = null!;

    public HsrRealTimeNotesCommandExecutor(IRealTimeNotesApiService<HsrRealTimeNotesData> apiService)
    {
        m_ApiService = apiService;
    }

    public ValueTask ExecuteAsync(params object?[] parameters)
    {
        throw new NotImplementedException();
    }

    public Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        throw new NotImplementedException();
    }
}
