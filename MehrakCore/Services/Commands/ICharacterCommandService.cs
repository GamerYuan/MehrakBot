#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICharacterCommandService<T> : ICommandExecutor where T : ICommandModule;
