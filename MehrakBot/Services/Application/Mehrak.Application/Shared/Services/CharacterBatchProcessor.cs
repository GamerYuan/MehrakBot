using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Application.Shared.Services;

internal sealed record CharacterRenderResult(string? FileName, CommandError? Error)
{
    internal static CharacterRenderResult Generated(string fileName) => new(fileName, null);
    internal static CharacterRenderResult Failed(CommandErrorCode code, CommandErrorTarget target) => new(null, new(code, target));
}

internal static class CharacterBatchProcessor
{
    internal static async Task<(IReadOnlyList<CharacterRenderResult?> Results, bool TimedOut)> ProcessAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task<CharacterRenderResult>> processAsync,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var degree = Math.Max(1, maxDegreeOfParallelism);
        var results = new CharacterRenderResult?[items.Count];
        using var semaphore = new SemaphoreSlim(degree);
        var timedOut = 0;

        var tasks = items.Select(async (item, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref timedOut) != 0) return;
                var result = await processAsync(item, cancellationToken);
                results[index] = result;
                if (result.Error?.Code == CommandErrorCode.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
                if (result.Error?.Code == CommandErrorCode.Timeout)
                    Interlocked.Exchange(ref timedOut, 1);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);

        return (results, timedOut != 0);
    }

    internal static CommandResult Complete(Game game, string gameUid, IReadOnlyList<CharacterCardItem> items, bool timedOut)
    {
        var generated = items.Count(x => x.Status == CharacterCardStatus.Generated);
        var state = generated == items.Count && generated > 0 ? OperationOutcome.Complete
            : generated > 0 ? OperationOutcome.Partial : OperationOutcome.Failed;
        var outcome = CharacterCardsOutcome.Create(game, gameUid, items, state);
        // A timeout remains the envelope's highest-priority failure even when cards completed.
        if (timedOut)
            return CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout,
                CommandErrorTarget.CharacterData, outcome);
        if (generated == 0)
            return CommandResult.Failure(CommandFailureReason.ApiError, CommandErrorCode.ApiUnavailable,
                CommandErrorTarget.CharacterData, outcome);
        return CommandResult.Success(outcome);
    }
}
