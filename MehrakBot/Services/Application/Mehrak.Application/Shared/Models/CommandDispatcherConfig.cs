namespace Mehrak.Application.Shared.Models;

public class CommandDispatcherConfig
{
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// Max number of characters processed concurrently within a single
    /// multi-character command. Caps how much of the dispatcher's global
    /// concurrency budget one user's request can consume. Default 2 keeps a
    /// 4-character request from monopolizing workers while still cutting
    /// wall-clock time roughly in half.
    /// </summary>
    public int MaxCharacterParallelism { get; set; } = 2;
}
