#region

using MehrakCore.Services.Commands.Executor;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICommandService<T> where T : ICommandExecutor;
