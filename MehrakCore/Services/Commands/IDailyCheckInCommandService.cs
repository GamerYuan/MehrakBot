#region

using MehrakCore.Modules;
using MehrakCore.Modules.Common;

#endregion

namespace MehrakCore.Services.Commands;

public interface IDailyCheckInCommandService<T> : ICommandExecutor where T : ICommandModule;
