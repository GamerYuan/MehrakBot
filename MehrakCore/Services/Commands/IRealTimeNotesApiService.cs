#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;

#endregion

namespace MehrakCore.Services.Commands;

public interface IRealTimeNotesApiService<T> : IApiService<IRealTimeNotesCommandExecutor<ICommandModule>>
    where T : IRealTimeNotesData
{
    Task<Result<T>> GetRealTimeNotesAsync(string roleId, string server, ulong ltuid, string ltoken);
}
