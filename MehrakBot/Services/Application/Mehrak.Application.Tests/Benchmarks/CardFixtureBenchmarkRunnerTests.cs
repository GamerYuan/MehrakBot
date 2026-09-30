#region

using System.Reflection;

#endregion

namespace Mehrak.Application.Tests.Benchmarks;

[TestFixture]
public sealed class CardFixtureBenchmarkRunnerTests
{
    [Test]
    public void DiscoverGoldenCases_ApplicationAssembly_FindsExpectedUniqueCorpus()
    {
        var cases = CardFixtureBenchmarkRunner.DiscoverGoldenCases(GetType().Assembly);

        Assert.That(cases, Has.Count.EqualTo(CardFixtureBenchmarkTests.ExpectedCaseCount));
        Assert.That(cases.Select(testCase => testCase.Identity), Is.Unique);
        Assert.That(cases.Count(testCase =>
            testCase.Identity.Contains("HsrCharListCardServiceTests", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(cases.Select(testCase => testCase.Method),
            Has.None.Property(nameof(MemberInfo.Name)).EqualTo(nameof(DiscoveryFixture.Excluded_ShouldMatchGoldenImage)));
    }

    [Test]
    public void DiscoverGoldenCases_ConcreteTestCases_UsesStableArgumentIdentityAndSkipsExplicitMethods()
    {
        var cases = CardFixtureBenchmarkRunner.DiscoverGoldenCases([typeof(DiscoveryFixture)]);

        Assert.That(cases, Has.Count.EqualTo(2));
        Assert.That(cases.Select(testCase => testCase.Identity), Is.EqualTo(new[]
        {
            $"{typeof(DiscoveryFixture).FullName}.Included_MatchesGoldenImage(\"first.json\")",
            $"{typeof(DiscoveryFixture).FullName}.Included_MatchesGoldenImage(\"second.json\")"
        }));
    }

    [Test]
    public async Task InvokeCaseAsync_FreshFixture_RunsNUnitLifecycleInOrder()
    {
        LifecycleFixture.Events.Clear();
        var method = typeof(LifecycleFixture).GetMethod(nameof(LifecycleFixture.GoldenCase),
            BindingFlags.Instance | BindingFlags.Public)!;
        var testCase = new CardFixtureBenchmarkCase(typeof(LifecycleFixture), method, ["data.json"], "case");

        await CardFixtureBenchmarkRunner.InvokeCaseAsync(testCase);

        Assert.That(LifecycleFixture.Events, Is.EqualTo(new[]
        {
            "one-time setup", "setup", "test:data.json", "teardown", "one-time teardown", "dispose"
        }));
    }

    [Test]
    public void InvokeCaseAsync_TestAndCleanupFail_ReportsPrimaryBeforeCleanup()
    {
        var method = typeof(FailingLifecycleFixture).GetMethod(nameof(FailingLifecycleFixture.GoldenCase),
            BindingFlags.Instance | BindingFlags.Public)!;
        var testCase = new CardFixtureBenchmarkCase(typeof(FailingLifecycleFixture), method, [], "case");

        var exception = Assert.ThrowsAsync<AggregateException>(() =>
            CardFixtureBenchmarkRunner.InvokeCaseAsync(testCase));

        Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(exception.InnerExceptions[0].Message, Is.EqualTo("primary"));
            Assert.That(exception.InnerExceptions[1].Message, Is.EqualTo("cleanup"));
        });
    }

    private abstract class DiscoveryFixture
    {
        [TestCase("second.json")]
        [TestCase("first.json")]
        [TestCase("explicit-case.json", Explicit = true)]
        public void Included_MatchesGoldenImage(string value)
        {
        }

        [Explicit]
        [TestCase("excluded.json")]
        public void Excluded_ShouldMatchGoldenImage(string value)
        {
        }
    }

    private sealed class LifecycleFixture : IDisposable
    {
        internal static readonly List<string> Events = [];

        [OneTimeSetUp]
        public void OneTimeSetUp() => Events.Add("one-time setup");

        [SetUp]
        public Task SetUp()
        {
            Events.Add("setup");
            return Task.CompletedTask;
        }

        public void GoldenCase(string value) => Events.Add($"test:{value}");

        [TearDown]
        public void TearDown() => Events.Add("teardown");

        [OneTimeTearDown]
        public ValueTask OneTimeTearDown()
        {
            Events.Add("one-time teardown");
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Events.Add("dispose");
    }

    private sealed class FailingLifecycleFixture
    {
        [SetUp]
        public void SetUp()
        {
        }

        public void GoldenCase() => throw new InvalidOperationException("primary");

        [TearDown]
        public void TearDown() => throw new InvalidOperationException("cleanup");
    }
}
