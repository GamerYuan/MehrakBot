#region

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Shared.Abstractions;
using NUnit.Framework;

#endregion

namespace Mehrak.Application.Tests.TestUtils;

/// <summary>
/// Test-only opt-in benchmark metrics for card rendering fixtures.
/// When the environment variable <c>MEHRAK_CARD_BENCHMARK_OUTPUT</c> is set to a
/// file path, <see cref="ObserveCardGenerationDuration"/> returns a real
/// Stopwatch-based timer that appends one JSONL line per card generation
/// (<c>{"test":...,"cardType":...,"elapsedMs":...}</c>) to that file. The caller
/// is responsible for starting each test process with a unique output path.
/// When the variable is unset or empty, all members are no-ops, matching the
/// previous <c>Mock.Of&lt;IApplicationMetrics&gt;()</c> behavior.
/// The JSONL payload contains only the NUnit test identity, the card type, and
/// the elapsed duration; no user IDs, card data, or private paths are recorded.
/// </summary>
public sealed class CardBenchmarkMetrics : IApplicationMetrics
{
    private const string OutputEnvironmentVariable = "MEHRAK_CARD_BENCHMARK_OUTPUT";

    private static readonly object FileAppendLock = new();

    private readonly string? m_OutputPath;

    private CardBenchmarkMetrics()
    {
        m_OutputPath = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
    }

    internal CardBenchmarkMetrics(string? outputPath)
    {
        m_OutputPath = outputPath;
    }

    /// <summary>
    /// Creates benchmark metrics for a card service test fixture. Reads
    /// <c>MEHRAK_CARD_BENCHMARK_OUTPUT</c> once per instance; when unset the
    /// instance behaves exactly like a no-op metrics mock.
    /// </summary>
    public static CardBenchmarkMetrics Create() => new();

    public IDisposable ObserveCardGenerationDuration(string cardType) =>
        string.IsNullOrWhiteSpace(m_OutputPath)
            ? NullTimer.Instance
            : new JsonLineTimer(m_OutputPath, cardType);

    public void TrackCharacterSelection(string game, string character)
    {
    }

    public IDisposable ObserveCommandDuration(string commandName) => NullTimer.Instance;

    public void RecordCommandDuration(string commandName, TimeSpan duration)
    {
    }

    public void RecordCardGenerationDuration(string cardType, TimeSpan duration)
    {
    }

    private sealed class NullTimer : IDisposable
    {
        public static readonly NullTimer Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Stopwatch-backed timer that appends a single JSONL benchmark record on
    /// disposal. Test identity is captured at timer start (the card generation
    /// boundary), disposal is idempotent, and appends are serialized process-wide.
    /// Append failures propagate so a misconfigured output path fails loudly
    /// instead of silently dropping benchmark samples.
    /// </summary>
    private sealed class JsonLineTimer(string outputPath, string cardType) : IDisposable
    {
        private readonly long m_StartTimestamp = Stopwatch.GetTimestamp();
        private readonly string m_TestFullName = TestContext.CurrentContext.Test?.FullName ?? "unknown";
        private int m_Disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_Disposed, 1) != 0)
                return;

            var elapsedMs = Math.Round(Stopwatch.GetElapsedTime(m_StartTimestamp).TotalMilliseconds, 3);
            var line = JsonSerializer.Serialize(
                new BenchmarkRecord(m_TestFullName, cardType, elapsedMs));

            lock (FileAppendLock)
            {
                File.AppendAllText(outputPath, line + Environment.NewLine);
            }
        }
    }

    private sealed record BenchmarkRecord(
        [property: JsonPropertyName("test")] string Test,
        [property: JsonPropertyName("cardType")] string CardType,
        [property: JsonPropertyName("elapsedMs")] double ElapsedMs);
}
