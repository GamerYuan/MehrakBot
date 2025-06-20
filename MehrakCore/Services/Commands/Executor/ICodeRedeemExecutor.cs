#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands.Executor;

public interface ICodeRedeemExecutor<T> : ICommandExecutor where T : ICommandModule;
