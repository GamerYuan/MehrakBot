#region

using Mehrak.Bot.Modules;


#endregion

namespace Mehrak.Bot.Executors.Executor;

public interface ICodeRedeemExecutor<T> : ICommandExecutor where T : ICommandModule;
