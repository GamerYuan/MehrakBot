#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands.Executor;

// ReSharper disable once UnusedTypeParameter
public interface ICharacterCommandExecutor<T> : ICommandExecutor where T : ICommandModule;
