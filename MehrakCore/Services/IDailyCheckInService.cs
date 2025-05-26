#region

#endregion

#region

using NetCord.Services;

#endregion

namespace MehrakCore.Services;

public interface IDailyCheckInService
{
    public Task CheckInAsync(IInteractionContext context, ulong ltuid, string ltoken);
}
