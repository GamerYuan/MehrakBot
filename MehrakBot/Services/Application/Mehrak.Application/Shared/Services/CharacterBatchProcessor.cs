using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;

namespace Mehrak.Application.Shared.Services;

internal static class CharacterBatchProcessor
{
    internal static async Task<(IReadOnlyList<Result<string>> Results, bool TimedOut)> ProcessAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task<Result<string>>> processAsync,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var degree = Math.Max(1, maxDegreeOfParallelism);
        var results = new Result<string>[items.Count];
        using var semaphore = new SemaphoreSlim(degree);
        var timedOut = false;

        var tasks = items.Select(async (item, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (timedOut) return;
                var result = await processAsync(item, cancellationToken);
                results[index] = result;
                if (result.StatusCode == StatusCode.Timeout)
                    timedOut = true;
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);

        return (results, timedOut);
    }
}
