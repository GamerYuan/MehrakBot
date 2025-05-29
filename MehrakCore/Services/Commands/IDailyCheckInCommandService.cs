#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands;

public interface IDailyCheckInCommandService<T> : ICommandExecutor where T : ICommandModule;