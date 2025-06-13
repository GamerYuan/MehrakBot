#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands.Executor;

public interface IRealTimeNotesCommandExecutor<T> : ICommandExecutor where T : ICommandModule;