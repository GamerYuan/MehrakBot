#region

using MehrakCore.Services.Commands.Executor;

#endregion

namespace Mehrak.Domain.Interfaces;

public interface ICommandService<T> where T : ICommandExecutor;
