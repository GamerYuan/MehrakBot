#region

#endregion

#region

using Mehrak.Domain.Services.Abstractions;
using MehrakCore.Models;
using MehrakCore.Services.Commands.Executor;

#endregion

namespace Mehrak.Application.Services;

public interface IDailyCheckInService : IApiService<IDailyCheckInCommandExecutor>
{
    public Task<ApiResult<(bool, string)>> CheckInAsync(ulong ltuid, string ltoken);
}
