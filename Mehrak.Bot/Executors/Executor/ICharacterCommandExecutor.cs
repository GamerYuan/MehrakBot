#region

using Mehrak.Bot.Modules;


#endregion

namespace Mehrak.Bot.Executors.Executor;

// ReSharper disable once UnusedTypeParameter
public interface ICharacterCommandExecutor<T> : ICommandExecutor where T : ICommandModule;