#region

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Grpc.Net.Client;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Cache.Abstractions;
using Mehrak.Domain.Image.Models;
using Mehrak.ImageProcessor.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace Mehrak.Application.Tests.Genshin.CharList;

/// <summary>
/// Public game-data-only roster entry used for the stable cold-benchmark fingerprint.
/// Account identifiers (game UID, nickname), credentials, and URLs are never included.
/// </summary>
internal sealed record CharListRosterEntry(
    int CharacterId,
    int CharacterLevel,
    int Rarity,
    string Element,
    int WeaponId,
    int WeaponLevel,
    int WeaponRarity,
    int Constellations = 0,
    int Refinement = 0);

/// <summary>
/// Test-only helpers for the Genshin charlist cold benchmark
/// (<c>MEHRAK_CHARLIST_BENCHMARK=1</c>). No network access here; the measured
/// real-API flow lives in <c>GenshinCharListApplicationServiceTests</c>.
/// </summary>
internal static class CharListBenchmarkSupport
{
    public const string BenchmarkEnvVar = "MEHRAK_CHARLIST_BENCHMARK";
    public const string BenchmarkLinePrefix = "CHARLIST_BENCHMARK";

    public static readonly string[] StaticElements =
        ["pyro", "hydro", "cryo", "electro", "anemo", "geo", "dendro"];

    public static bool IsBenchmarkMode() =>
        string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);

    /// <summary>
    /// Stable roster fingerprint (lowercase hex SHA256) over sorted public
    /// game-data fields only. Excludes account identifiers, credentials, and URLs.
    /// </summary>
    public static string ComputeRosterFingerprint(IEnumerable<CharListRosterEntry> roster)
    {
        var canonical = string.Join("|", roster
            .OrderBy(x => x.CharacterId)
            .ThenBy(x => x.WeaponId)
            .Select(x =>
                $"{x.CharacterId}:{x.CharacterLevel}:{x.Rarity}:{x.Element}:{x.WeaponId}:{x.WeaponLevel}:{x.WeaponRarity}:{x.Constellations}:{x.Refinement}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// Builds the single-line secret-safe benchmark JSON payload (without the prefix).
    /// Only timings, counts, and the roster fingerprint are included.
    /// </summary>
    public static string BuildBenchmarkLine(
        string status,
        double totalMs,
        double cardMs,
        int characterCount,
        int uniqueWeaponCount,
        int level40Count,
        string rosterFingerprint,
        int initialDynamicAssetCount,
        long outputBytes,
        int avatarStored,
        int weaponBaseStored,
        int weaponAscendedStored,
        int expectedAscended,
        int missingAscended)
    {
        var payload = new SortedDictionary<string, object?>
        {
            ["status"] = status,
            ["total_ms"] = Math.Round(totalMs, 1),
            ["card_ms"] = Math.Round(cardMs, 1),
            ["character_count"] = characterCount,
            ["unique_weapon_count"] = uniqueWeaponCount,
            ["level40_count"] = level40Count,
            ["roster_fingerprint"] = rosterFingerprint,
            ["initial_dynamic_asset_count"] = initialDynamicAssetCount,
            ["output_bytes"] = outputBytes,
            ["avatar_stored"] = avatarStored,
            ["weapon_base_stored"] = weaponBaseStored,
            ["weapon_ascended_stored"] = weaponAscendedStored,
            ["expected_ascended"] = expectedAscended,
            ["missing_ascended"] = missingAscended
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string BuildBenchmarkErrorLine() =>
        BuildBenchmarkLine("error", 0, -1, 0, 0, 0, "none", 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Seeds only required static icons in a newly created test bucket.
    /// Dynamic assets are deliberately absent for the cold benchmark.
    /// </summary>
    public static async Task SeedStaticAssetsAsync(
        IAmazonS3 s3,
        string bucket,
        string assetsRoot,
        CancellationToken cancellationToken = default)
    {
        foreach (var element in StaticElements)
        {
            var key = string.Format(FileNameFormat.Genshin.ElementName, element);
            var sourcePath = Path.Combine(assetsRoot, "Genshin", $"element_{element}.png");
            await using var stream = File.OpenRead(sourcePath);
            var put = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
                ContentType = FileNameFormat.PngContentType
            };
            await s3.PutObjectAsync(put, cancellationToken);
        }
    }

    public static async Task<List<string>> ListKeysAsync(
        IAmazonS3 s3, string bucket, string prefix, CancellationToken cancellationToken = default)
    {
        var keys = new List<string>();
        string? continuationToken = null;
        do
        {
            var response = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = 1000
            }, cancellationToken);
            keys.AddRange((response.S3Objects ?? []).Select(x => x.Key));
            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken != null);
        return keys;
    }

}

/// <summary>
/// Real metrics timer adapter for the benchmark: genuinely observes the card
/// service duration via <c>ObserveCardGenerationDuration</c>. Command-level
/// metrics are out of scope for this benchmark.
/// </summary>
internal sealed class CharListBenchmarkMetrics : IApplicationMetrics
{
    public double LastCardDurationMs { get; private set; } = -1;

    public void TrackCharacterSelection(string game, string character)
    {
    }

    public IDisposable ObserveCommandDuration(string commandName) => NullScope.Instance;

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
    }

    public IDisposable ObserveCardGenerationDuration(string cardType) => new CardTimer(this);

    public void RecordCardGenerationDuration(string cardType, TimeSpan duration) =>
        LastCardDurationMs = duration.TotalMilliseconds;

    private sealed class CardTimer(CharListBenchmarkMetrics m_Metrics) : IDisposable
    {
        private readonly long m_Start = Stopwatch.GetTimestamp();
        private bool m_Disposed;

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;
            m_Metrics.RecordCardGenerationDuration(string.Empty, Stopwatch.GetElapsedTime(m_Start));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Fresh per-run in-memory API cache. Starts empty so every benchmark run
/// observes a consistent cold (no-cached-data) state within the run.
/// </summary>
internal sealed class InMemoryBenchmarkCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, object?> m_Entries = new(StringComparer.Ordinal);

    public Task SetAsync<T>(ICacheEntry<T> entry, CancellationToken cancellationToken = default)
    {
        m_Entries[entry.Key] = entry.Value;
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (m_Entries.TryGetValue(key, out var value) && value is T typed)
            return Task.FromResult<T?>(typed);
        return Task.FromResult<T?>(default);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        m_Entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-only HTTP client factory for real outbound HoYoLAB/wiki traffic.
/// A single shared client is used for the whole benchmark run.
/// </summary>
internal sealed class BenchmarkHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly HttpClient m_Client = new();

    public HttpClient CreateClient(string name) => m_Client;

    public void Dispose() => m_Client.Dispose();
}

/// <summary>
/// Inert NSFW dependency for the benchmark weapon gRPC host. The weapon
/// endpoint never touches the classifier, so no ONNX model is booted.
/// Any accidental use fails loudly instead of silently.
/// </summary>
internal sealed class InertNsfwClassifier : INsfwClassifier
{
    public NsfwClassificationResult Classify(byte[] imageData) =>
        throw new NotSupportedException("NSFW classification is excluded from the charlist cold benchmark.");
}

/// <summary>
/// Loopback-only ephemeral-port Kestrel gRPC host serving the real production
/// <c>GrpcImageProcessorService</c> with the real production
/// <c>GenshinWeaponImageProcessor</c> and <c>PortraitImageMatcher</c>.
/// Logging providers are cleared so no request data is retained in logs.
/// No external services, AppHost, or migrations are involved.
/// </summary>
internal sealed class WeaponGrpcBenchmarkHost : IAsyncDisposable
{
    private WebApplication? m_App;
    private bool m_Disposed;

    public GrpcChannel Channel { get; private set; } = null!;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 12 * 1024 * 1024;
            options.MaxSendMessageSize = 12 * 1024 * 1024;
        });
        builder.Services.AddSingleton<INsfwClassifier, InertNsfwClassifier>();
        builder.Services.AddSingleton<GenshinWeaponImageProcessor>();
        builder.Services.AddSingleton<PortraitImageMatcher>();
        builder.Services.AddSingleton<ILogger<GrpcImageProcessorService>>(
            NullLogger<GrpcImageProcessorService>.Instance);
        builder.Services.AddSingleton<GrpcImageProcessorService>();

        m_App = builder.Build();
        m_App.MapGrpcService<GrpcImageProcessorService>();
        await m_App.StartAsync(cancellationToken);

        var addresses = m_App.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(x => x.StartsWith("http://", StringComparison.Ordinal));
        if (string.IsNullOrEmpty(address))
            throw new InvalidOperationException("Benchmark gRPC host produced no loopback address.");
        Channel = GrpcChannel.ForAddress(address);
    }

    public async ValueTask DisposeAsync()
    {
        if (m_Disposed)
            return;
        m_Disposed = true;
        Channel?.Dispose();
        if (m_App != null)
        {
            await m_App.StopAsync();
            await m_App.DisposeAsync();
        }
    }
}
