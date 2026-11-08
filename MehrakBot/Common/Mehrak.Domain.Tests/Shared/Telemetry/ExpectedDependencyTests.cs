using Mehrak.Domain.Shared.Telemetry;

namespace Mehrak.Domain.Tests.Shared.Telemetry;

[TestFixture]
public sealed class ExpectedDependencyTests
{
    [Test]
    public void Begin_RestoresPreviousIntentAfterDisposal()
    {
        Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));

        using (ExpectedDependency.Begin(ExpectedDependencyKind.ExistenceProbe))
        {
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.ExistenceProbe));

            using (ExpectedDependency.Begin(ExpectedDependencyKind.WikiContentLookup))
            {
                Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.WikiContentLookup));
            }

            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.ExistenceProbe));
        }

        Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
    }

    [Test]
    public async Task Begin_IsIsolatedBetweenConcurrentAsynchronousFlows()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var probe = Task.Run(async () =>
        {
            using var scope = ExpectedDependency.Begin(ExpectedDependencyKind.ExistenceProbe);
            await gate.Task;
            return ExpectedDependency.Current;
        });

        var wiki = Task.Run(async () =>
        {
            using var scope = ExpectedDependency.Begin(ExpectedDependencyKind.WikiContentLookup);
            await gate.Task;
            return ExpectedDependency.Current;
        });

        gate.SetResult();
        var observed = await Task.WhenAll(probe, wiki).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(observed[0], Is.EqualTo(ExpectedDependencyKind.ExistenceProbe));
            Assert.That(observed[1], Is.EqualTo(ExpectedDependencyKind.WikiContentLookup));
            Assert.That(ExpectedDependency.Current, Is.EqualTo(ExpectedDependencyKind.None));
        });
    }

    [TestCase(ExpectedDependencyKind.ExistenceProbe, ExpectedDependency.ExistenceProbeKind)]
    [TestCase(ExpectedDependencyKind.WikiContentLookup, ExpectedDependency.WikiContentKind)]
    public void KindValue_IsBoundedForClassifiedKinds(ExpectedDependencyKind kind, string expectedTagValue)
    {
        Assert.That(ExpectedDependency.KindValue(kind), Is.EqualTo(expectedTagValue));
        Assert.That(ExpectedDependency.KindValue(ExpectedDependencyKind.None), Is.Null);
    }
}
