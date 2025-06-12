#region

#endregion

#region

using MehrakCore.Models;
using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands;

public interface IDailyCheckInService : IApiService
{
    public Task CheckInAsync(IInteractionContext context, UserModel user, uint profile, ulong ltuid, string ltoken);
}
