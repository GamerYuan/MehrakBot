#region

using System.Text.Json;

#endregion

namespace Mehrak.Application.Tests.TestUtils;

[TestFixture]
[NonParallelizable]
public sealed class CardBenchmarkMetricsTests
{
    private const string CardType = "genshin character";

    private string? m_OriginalEnvironmentValue;
    private string m_OutputDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        m_OriginalEnvironmentValue = Environment.GetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT");
        m_OutputDirectory = Path.Combine(Path.GetTempPath(), "mehrak-card-benchmark-tests",
            Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", m_OriginalEnvironmentValue);
        if (Directory.Exists(m_OutputDirectory))
            Directory.Delete(m_OutputDirectory, true);
    }

    [Test]
    public void Create_EnvironmentUnset_TimerIsNoOpWithoutFileWrites()
    {
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", null);

        var metrics = CardBenchmarkMetrics.Create();

        var timer = metrics.ObserveCardGenerationDuration(CardType);
        Assert.That(timer, Is.Not.Null);
        Assert.DoesNotThrow(timer.Dispose);
        Assert.That(File.Exists("nonexistent-benchmark-output.jsonl"), Is.False);
    }

    [Test]
    public void Create_EnvironmentEmpty_TimerIsNoOpWithoutFileWrites()
    {
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", string.Empty);

        var metrics = CardBenchmarkMetrics.Create();

        var timer = metrics.ObserveCardGenerationDuration(CardType);
        Assert.That(timer, Is.Not.Null);
        Assert.DoesNotThrow(timer.Dispose);
        Assert.That(File.Exists("nonexistent-benchmark-output.jsonl"), Is.False);
    }

    [Test]
    public async Task ObserveCardGenerationDuration_EnvironmentSet_AppendsJsonlRecord()
    {
        var outputPath = await CreateTempOutputPath();
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", outputPath);

        var metrics = CardBenchmarkMetrics.Create();

        using (metrics.ObserveCardGenerationDuration(CardType))
        {
            Thread.Sleep(10);
        }

        Assert.That(File.Exists(outputPath), Is.True, "JSONL output file should exist after disposal");

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Has.Length.EqualTo(1), "exactly one JSONL record should be appended");

        var record = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.That(record.GetProperty("test").GetString(),
            Does.StartWith("Mehrak.Application.Tests.TestUtils.CardBenchmarkMetricsTests"));
        Assert.That(record.GetProperty("test").GetString(),
            Does.Contain("ObserveCardGenerationDuration_EnvironmentSet_AppendsJsonlRecord"));
        Assert.That(record.GetProperty("cardType").GetString(), Is.EqualTo(CardType));
        Assert.That(record.GetProperty("elapsedMs").GetDouble(), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Dispose_CalledTwice_AppendsOnlyOneRecord()
    {
        var outputPath = await CreateTempOutputPath();
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", outputPath);

        var metrics = CardBenchmarkMetrics.Create();
        var timer = metrics.ObserveCardGenerationDuration(CardType);

        timer.Dispose();
        timer.Dispose();

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Has.Length.EqualTo(1), "idempotent disposal should append exactly one record");
        var record = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.That(record.GetProperty("cardType").GetString(), Is.EqualTo(CardType));
    }

    [Test]
    public async Task Dispose_SameTimerConcurrently_AppendsOnlyOneRecord()
    {
        var outputPath = await CreateTempOutputPath();
        var timer = new CardBenchmarkMetrics(outputPath).ObserveCardGenerationDuration(CardType);

        Parallel.For(0, 32, _ => timer.Dispose());

        Assert.That(await File.ReadAllLinesAsync(outputPath), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Dispose_MultipleTimersConcurrently_WritesWellFormedJsonl()
    {
        var outputPath = await CreateTempOutputPath();
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", outputPath);

        var metrics = CardBenchmarkMetrics.Create();
        var timers = Enumerable.Range(0, 16)
            .Select(_ => metrics.ObserveCardGenerationDuration(CardType))
            .ToArray();

        Parallel.ForEach(timers, timer => timer.Dispose());

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Has.Length.EqualTo(16), "all concurrent timers should append one record each");
        Assert.That(lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("cardType").GetString()),
            Has.All.EqualTo(CardType));
        Assert.That(lines.Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("elapsedMs").GetDouble()),
            Has.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Dispose_OutputDirectoryMissing_ThrowsLoudly()
    {
        var outputPath = Path.Combine(m_OutputDirectory, "missing-directory", "benchmark.jsonl");
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", outputPath);

        var metrics = CardBenchmarkMetrics.Create();

        var timer = metrics.ObserveCardGenerationDuration(CardType);
        Assert.Throws<DirectoryNotFoundException>(timer.Dispose);
    }

    [Test]
    public async Task Timer_ElapsedMsReflectsObservedDuration_ExceedsTrackedWait()
    {
        var outputPath = await CreateTempOutputPath();
        Environment.SetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT", outputPath);

        var metrics = CardBenchmarkMetrics.Create();

        using (metrics.ObserveCardGenerationDuration(CardType))
        {
            Thread.Sleep(50);
        }

        var lines = await File.ReadAllLinesAsync(outputPath);
        var elapsedMs = JsonSerializer.Deserialize<JsonElement>(lines[0]).GetProperty("elapsedMs").GetDouble();
        Assert.That(elapsedMs, Is.GreaterThanOrEqualTo(40),
            "captured elapsedMs should reflect the observed card generation wait");
    }

    [Test]
    public async Task BeginScope_OverridesRunnerTestIdentity()
    {
        var outputPath = await CreateTempOutputPath();
        var metrics = new CardBenchmarkMetrics(outputPath);

        using (CardBenchmarkMetrics.BeginScope("Original.Fixture.GoldenCase(\"data.json\")"))
        using (metrics.ObserveCardGenerationDuration(CardType))
        {
        }

        var record = JsonSerializer.Deserialize<JsonElement>((await File.ReadAllLinesAsync(outputPath)).Single());
        Assert.That(record.GetProperty("test").GetString(),
            Is.EqualTo("Original.Fixture.GoldenCase(\"data.json\")"));
    }

    [Test]
    public async Task BeginScope_SuppressSamples_DoesNotWriteWarmupRecordAndRestoresOuterScope()
    {
        var outputPath = await CreateTempOutputPath();
        var metrics = new CardBenchmarkMetrics(outputPath);

        using (CardBenchmarkMetrics.BeginScope("measured-case"))
        {
            using (CardBenchmarkMetrics.BeginScope("warmup-case", true))
            using (metrics.ObserveCardGenerationDuration(CardType))
            {
            }

            using (metrics.ObserveCardGenerationDuration(CardType))
            {
            }
        }

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.That(lines, Has.Length.EqualTo(1));
        var record = JsonSerializer.Deserialize<JsonElement>(lines.Single());
        Assert.That(record.GetProperty("test").GetString(), Is.EqualTo("measured-case"));
    }

    [Test]
    public async Task BeginScope_BackgroundFlagsFlowAcrossAsyncWorkAndRestoreNestedScope()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CardBenchmarkMetrics.IsBenchmarkScope, Is.False);
            Assert.That(CardBenchmarkMetrics.UseLegacyBackgrounds, Is.False);
        });

        using (CardBenchmarkMetrics.BeginScope("legacy-case", useLegacyBackgrounds: true))
        {
            await Task.Yield();
            Assert.Multiple(() =>
            {
                Assert.That(CardBenchmarkMetrics.IsBenchmarkScope, Is.True);
                Assert.That(CardBenchmarkMetrics.UseLegacyBackgrounds, Is.True);
            });

            using (CardBenchmarkMetrics.BeginScope("cached-case"))
            {
                await Task.Yield();
                Assert.Multiple(() =>
                {
                    Assert.That(CardBenchmarkMetrics.IsBenchmarkScope, Is.True);
                    Assert.That(CardBenchmarkMetrics.UseLegacyBackgrounds, Is.False);
                });
            }

            Assert.That(CardBenchmarkMetrics.UseLegacyBackgrounds, Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(CardBenchmarkMetrics.IsBenchmarkScope, Is.False);
            Assert.That(CardBenchmarkMetrics.UseLegacyBackgrounds, Is.False);
        });
    }

    private Task<string> CreateTempOutputPath()
    {
        Directory.CreateDirectory(m_OutputDirectory);
        return Task.FromResult(Path.Combine(m_OutputDirectory, "benchmark.jsonl"));
    }
}
