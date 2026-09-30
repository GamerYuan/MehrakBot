#region

using System.Text.Json;

#endregion

namespace Mehrak.Application.Tests.Genshin.CharList;

/// <summary>
/// Narrow no-network tests for the cold-benchmark helpers. These never touch
/// the API, S3, or the gRPC host; they only verify secret-safe behavior of the
/// benchmark support types (fingerprinting, metrics adapter, JSON shape,
/// per-run cache, inert classifier, env flag).
/// </summary>
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal class CharListBenchmarkSupportTests
{
    private static List<CharListRosterEntry> CreateRoster() =>
    [
        new(10000002, 90, 5, "Anemo", 11401, 90, 4),
        new(10000089, 80, 5, "Hydro", 11101, 40, 4),
        new(10000015, 70, 4, "Pyro", 12201, 50, 3)
    ];

    [Test]
    public void IsBenchmarkMode_OnlyEnabledByExactFlagValue()
    {
        var previous = Environment.GetEnvironmentVariable(CharListBenchmarkSupport.BenchmarkEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(CharListBenchmarkSupport.BenchmarkEnvVar, "1");
            Assert.That(CharListBenchmarkSupport.IsBenchmarkMode(), Is.True);

            Environment.SetEnvironmentVariable(CharListBenchmarkSupport.BenchmarkEnvVar, "0");
            Assert.That(CharListBenchmarkSupport.IsBenchmarkMode(), Is.False);

            Environment.SetEnvironmentVariable(CharListBenchmarkSupport.BenchmarkEnvVar, null);
            Assert.That(CharListBenchmarkSupport.IsBenchmarkMode(), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CharListBenchmarkSupport.BenchmarkEnvVar, previous);
        }
    }

    [Test]
    public void ComputeRosterFingerprint_IsStableAndOrderIndependent()
    {
        var forward = CharListBenchmarkSupport.ComputeRosterFingerprint(CreateRoster());
        var reversed = CharListBenchmarkSupport.ComputeRosterFingerprint(CreateRoster().AsEnumerable().Reverse());

        Assert.That(forward, Is.EqualTo(reversed));
        Assert.That(forward, Has.Length.EqualTo(64));
        Assert.That(forward, Is.EqualTo(forward.ToLowerInvariant()));
    }

    [Test]
    public void ComputeRosterFingerprint_ChangesWithRosterData()
    {
        var baseline = CharListBenchmarkSupport.ComputeRosterFingerprint(CreateRoster());
        var changed = CreateRoster();
        changed[0] = changed[0] with { WeaponLevel = 80 };

        Assert.That(CharListBenchmarkSupport.ComputeRosterFingerprint(changed), Is.Not.EqualTo(baseline));
    }

    [Test]
    public void ComputeRosterFingerprint_ExcludesAccountIdentifiers()
    {
        var fingerprint = CharListBenchmarkSupport.ComputeRosterFingerprint(CreateRoster());

        Assert.That(fingerprint, Does.Not.Contain("800000000"));
        Assert.That(fingerprint, Does.Not.Contain("TestPlayer"));
        Assert.That(fingerprint, Does.Not.Contain("token"));
    }

    [Test]
    public void BenchmarkMetrics_RecordsCardDuration()
    {
        var metrics = new CharListBenchmarkMetrics();
        Assert.That(metrics.LastCardDurationMs, Is.EqualTo(-1));

        using (metrics.ObserveCardGenerationDuration("genshin charlist"))
        {
        }

        Assert.That(metrics.LastCardDurationMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void BuildBenchmarkLine_IsSingleLineWithExpectedShapeAndNoSecrets()
    {
        var line = CharListBenchmarkSupport.BuildBenchmarkLine(
            "ok", 1234.5, 999.9, 10, 8, 2, new string('a', 64), 0, 123456, 10, 8, 5, 6, 1);

        Assert.That(line.Any(x => x is '\r' or '\n'), Is.False);

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);
        Assert.That(payload, Is.Not.Null);
        foreach (var key in new[]
                 {
                     "status", "total_ms", "card_ms", "character_count", "unique_weapon_count",
                     "level40_count", "roster_fingerprint", "initial_dynamic_asset_count",
                     "output_bytes", "avatar_stored", "weapon_base_stored",
                     "weapon_ascended_stored", "expected_ascended", "missing_ascended"
                 })
        {
            Assert.That(payload!, Contains.Key(key), $"Missing benchmark field: {key}");
        }

        Assert.That(line, Does.Not.Contain("800000000"));
        Assert.That(line, Does.Not.Contain("TestPlayer"));
        Assert.That(line, Does.Not.Contain("ltoken"));
        Assert.That(line, Does.Not.Contain("http"));
    }

    [Test]
    public void BuildBenchmarkErrorLine_IsSecretSafeSingleLine()
    {
        var line = CharListBenchmarkSupport.BuildBenchmarkErrorLine();

        Assert.That(line.Any(x => x is '\r' or '\n'), Is.False);
        Assert.That(line, Does.Contain("\"status\":\"error\""));
    }

    [Test]
    public async Task InMemoryBenchmarkCache_StartsEmptyAndRoundTripsWithinRun()
    {
        var cache = new InMemoryBenchmarkCacheService();

        Assert.That(await cache.GetAsync<string>("missing"), Is.Null);

        await cache.SetAsync(new CacheEntry("k", "v", TimeSpan.FromMinutes(1)));
        Assert.That(await cache.GetAsync<string>("k"), Is.EqualTo("v"));

        var fresh = new InMemoryBenchmarkCacheService();
        Assert.That(await fresh.GetAsync<string>("k"), Is.Null);
    }

    [Test]
    public void InertNsfwClassifier_ThrowsWithoutBootingModel()
    {
        var classifier = new InertNsfwClassifier();

        Assert.That(() => classifier.Classify([1, 2, 3]), Throws.TypeOf<NotSupportedException>());
    }

    private sealed record CacheEntry(string Key, string Value, TimeSpan ExpirationTime)
        : Mehrak.Domain.Cache.Abstractions.ICacheEntry<string>;
}
