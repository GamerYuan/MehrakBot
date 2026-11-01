#region

using System.Collections.Concurrent;
using Mehrak.Application.Shared.Services;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;

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
            (item, ct) => Task.FromResult(CharacterRenderResult.Generated(item.ToString())),
            maxDegreeOfParallelism: 4,
            CancellationToken.None);

        Assert.That(timedOut, Is.False);
        Assert.That(results.Count, Is.EqualTo(6));
        for (var i = 0; i < 6; i++)
        {
            Assert.That(results[i]!.FileName, Is.EqualTo(i.ToString()));
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
                return CharacterRenderResult.Generated(item.ToString());
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
                    return Task.FromResult(CharacterRenderResult.Failed(CommandErrorCode.Timeout, CommandErrorTarget.CharacterImage));
                return Task.FromResult(CharacterRenderResult.Generated(item.ToString()));
            },
            maxDegreeOfParallelism: 2,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(timedOut, Is.True);
            Assert.That(processed.Count, Is.LessThan(items.Count), "Timeout must leave queued work unprocessed.");
        });
    }

    [Test]
    public async Task ProcessAsync_DegreeClampedToOne_NeverDeadlocks()
    {
        var items = Enumerable.Range(0, 5).ToList();

        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            items,
            (item, ct) => Task.FromResult(CharacterRenderResult.Generated(item.ToString())),
            maxDegreeOfParallelism: 0,
            CancellationToken.None);

        Assert.That(timedOut, Is.False);
        Assert.That(results.Count, Is.EqualTo(5));
        for (var i = 0; i < 5; i++)
        {
            Assert.That(results[i]!.FileName, Is.EqualTo(i.ToString()));
        }
    }
    [Test]
    public async Task ProcessAsync_Timeout_PreservesCompletedSlotsAndLeavesSkippedSlotsNull()
    {
        var (results, timedOut) = await CharacterBatchProcessor.ProcessAsync(
            new[] { 0, 1, 2 },
            (item, _) => Task.FromResult(item == 1
                ? CharacterRenderResult.Failed(CommandErrorCode.Timeout, CommandErrorTarget.CharacterImage)
                : CharacterRenderResult.Generated("final-stock-key.jpg")),
            maxDegreeOfParallelism: 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(timedOut, Is.True);
            Assert.That(results[0]!.FileName, Is.EqualTo("final-stock-key.jpg"));
            Assert.That(results[1]!.Error!.Code, Is.EqualTo(CommandErrorCode.Timeout));
            Assert.That(results[2], Is.Null);
        });
    }

    [Test]
    public void Complete_TimeoutRetainsPartialFactsAndUnattemptedSlots_WithPriorityOverSuccess()
    {
        var items = new[]
        {
            CharacterCardItem.Generated("alias", "Character", 1, StorageReference.Attachment("final-stock-key.jpg")),
            CharacterCardItem.Failed("Second", new(CommandErrorCode.Timeout, CommandErrorTarget.CharacterImage)),
            CharacterCardItem.NotAttempted("Third")
        };
        var result = CharacterBatchProcessor.Complete(Game.Genshin, "uid", items, timedOut: true);
        var outcome = (CharacterCardsOutcome)result.Outcome!;
        var roundTrip = (CharacterCardsOutcome)result.ToProto().ToDomain().Outcome!;

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.Timeout));
            Assert.That(outcome.State, Is.EqualTo(OperationOutcome.Partial));
            Assert.That(outcome.Items[2].Status, Is.EqualTo(CharacterCardStatus.NotAttempted));
            Assert.That(outcome.Items[2].Error, Is.Null);
            Assert.That(roundTrip.Items, Is.EqualTo(outcome.Items));
            Assert.That(roundTrip.Items[0].Image!.StorageFileName, Is.EqualTo("final-stock-key.jpg"));
        });
    }

    [Test]
    public void Complete_MixedNotFoundAndGenerated_IsPartial()
    {
        var result = CharacterBatchProcessor.Complete(Game.HonkaiStarRail, "uid",
            [CharacterCardItem.NotFound("Missing"),
             CharacterCardItem.Generated("alias", "Character", 1, StorageReference.Attachment("cached.jpg"))], false);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(((CharacterCardsOutcome)result.Outcome!).State, Is.EqualTo(OperationOutcome.Partial));
    }

    [Test]
    public void Complete_AllGenerationFailed_IsFailedAndKeepsCodedItemError()
    {
        var result = CharacterBatchProcessor.Complete(Game.Genshin, "uid",
            [CharacterCardItem.Failed("Character", new(CommandErrorCode.AttachmentStoreFailed, CommandErrorTarget.Attachment))], false);
        Assert.That(result.IsSuccess, Is.False);
        var outcome = (CharacterCardsOutcome)result.Outcome!;
        Assert.That(outcome.State, Is.EqualTo(OperationOutcome.Failed));
        Assert.That(outcome.Items[0].Error!.Code, Is.EqualTo(CommandErrorCode.AttachmentStoreFailed));
    }

}
