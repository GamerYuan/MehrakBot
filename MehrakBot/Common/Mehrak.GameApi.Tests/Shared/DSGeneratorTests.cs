using System.Text.RegularExpressions;
using Mehrak.GameApi.Shared;

namespace Mehrak.GameApi.Tests.Shared;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class DSGeneratorTests
{
    private static readonly Regex DsFormatRegex = new(@"^\d+,[A-Za-z0-9]{6},[0-9a-f]{32}$", RegexOptions.Compiled);

    [Test]
    public void GenerateDS_ReturnsExpectedFormat()
    {
        var ds = DSGenerator.GenerateDS();

        Assert.That(ds, Does.Match(DsFormatRegex));
    }

    [Test]
    public void GenerateDS_ConcurrentCalls_DoNotThrow()
    {
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => DSGenerator.GenerateDS()));

        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

        Assert.That(results, Has.Length.EqualTo(200));
        Assert.That(results, Has.All.Matches(DsFormatRegex));
    }
}
