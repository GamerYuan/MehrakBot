namespace Mehrak.Application.Shared.Models;

public class CommandDispatcherConfig
{
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// Max number of characters processed concurrently within a single
    /// multi-character command. Caps how much of the dispatcher's global
    /// concurrency budget one user's request can consume. Default 4 balances
    /// throughput against resource usage for typical multi-character requests.
    /// </summary>
    public int MaxCharacterParallelism { get; set; } = 4;
}
