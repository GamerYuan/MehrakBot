#region

#endregion

#region

using MehrakCore.Models;
using MehrakCore.Services.Commands.Executor;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands;

public interface IDailyCheckInService : IApiService<IDailyCheckInCommandExecutor>
{
    public Task CheckInAsync(IInteractionContext context, UserModel user, uint profile, ulong ltuid, string ltoken);
}
