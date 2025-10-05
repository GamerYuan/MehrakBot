#region

#endregion

#region

using MehrakCore.Models;
using MehrakCore.Services.Commands.Executor;

#endregion

namespace MehrakCore.Services.Commands;

public interface IDailyCheckInService : IApiService<IDailyCheckInCommandExecutor>
{
    public Task<ApiResult<(bool, string)>> CheckInAsync(ulong ltuid, string ltoken);
}
