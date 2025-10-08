#region

using Mehrak.Bot.Modules;


#endregion

namespace Mehrak.Bot.Executors.Executor;

public interface IRealTimeNotesCommandExecutor<T> : ICommandExecutor where T : ICommandModule;