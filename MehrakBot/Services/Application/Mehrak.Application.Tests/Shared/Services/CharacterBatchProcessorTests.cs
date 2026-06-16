#region

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using NUnit.Framework;

#endregion

namespace Mehrak.Application.Tests.Shared.Services;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CharacterBatchProcessorTests
{
    [Test]
    public async Task ProcessAsync_PreservesOutputOrder()
    {
        var items = Enumerable.Range(0, 6).ToList();

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            items,
            (item, ct) => Task.FromResult(Result<string>.Success(item.ToString())),
            maxDegreeOfParallelism: 4,
            CancellationToken.None);

        Assert.That(timedOut, Is.False);
        Assert.That(results.Count, Is.EqualTo(6));
        for (var i = 0; i < 6; i++)
        {
            Assert.That(results[i].Data, Is.EqualTo(i.ToString()));
        }
    }

    [Test]
    public async Task ProcessAsync_RespectsMaxDegreeOfParallelism()
    {
        var items = Enumerable.Range(0, 10).ToList();
        var peakCount = 0;
        var currentCount = 0;
        var lockObj = new object();

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            items,
            async (item, ct) =>
            {
                var local = Interlocked.Increment(ref currentCount);
                lock (lockObj)
                {
                    if (local > peakCount) peakCount = local;
                }
                await Task.Delay(50, ct);
                Interlocked.Decrement(ref currentCount);
                return Result<string>.Success(item.ToString());
            },
            maxDegreeOfParallelism: 2,
            CancellationToken.None);

        Assert.That(timedOut, Is.False);
        Assert.That(results.Count, Is.EqualTo(10));
        Assert.That(peakCount, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task ProcessAsync_StopsOnTimeout_AndSetsTimedOut()
    {
        var processed = new ConcurrentBag<int>();
        var items = Enumerable.Range(0, 10).ToList();

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            items,
            (item, ct) =>
            {
                processed.Add(item);
                if (item == 3)
                    return Task.FromResult(Result<string>.Failure(StatusCode.Timeout, "timed out"));
                return Task.FromResult(Result<string>.Success(item.ToString()));
            },
            maxDegreeOfParallelism: 2,
            CancellationToken.None);

        Assert.That(timedOut, Is.True);
    }

    [Test]
    public async Task ProcessAsync_DegreeClampedToOne_NeverDeadlocks()
    {
        var items = Enumerable.Range(0, 5).ToList();

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            items,
            (item, ct) => Task.FromResult(Result<string>.Success(item.ToString())),
            maxDegreeOfParallelism: 0,
            CancellationToken.None);

        Assert.That(timedOut, Is.False);
        Assert.That(results.Count, Is.EqualTo(5));
        for (var i = 0; i < 5; i++)
        {
            Assert.That(results[i].Data, Is.EqualTo(i.ToString()));
        }
    }
}
