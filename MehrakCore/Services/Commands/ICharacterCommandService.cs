#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands;

// ReSharper disable once UnusedTypeParameter
public interface ICharacterCommandService<T> : ICommandExecutor where T : ICommandModule;
