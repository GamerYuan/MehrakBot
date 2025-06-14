#region

#endregion

#region

using NetCord.Services;

#endregion

namespace MehrakCore.Services.Commands.Executor;

public interface ICommandExecutor
{
    public IInteractionContext Context { get; set; }

    /// <summary>
    /// Executes the command with the given parameters.
    /// </summary>
    /// <param name="parameters">The parameters for the command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask ExecuteAsync(params object?[] parameters);
}
