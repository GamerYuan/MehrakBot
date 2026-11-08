namespace Mehrak.Domain.Shared.Telemetry;

/// <summary>
/// Describes a dependency call whose negative response is a normal domain answer rather than a fault.
/// </summary>
public enum ExpectedDependencyKind
{
    /// <summary>No expected-miss classification; a negative response is a genuine failure.</summary>
    None = 0,

    /// <summary>An object-existence probe where a 404 is the negative answer.</summary>
    ExistenceProbe = 1,

    /// <summary>
    /// A HoYoWiki content lookup where a content miss is a normal answer. An HTTP 404 normalizes the request span;
    /// a retcode 404 returned with HTTP 200 does not create an error span and only produces a warning log.
    /// </summary>
    WikiContentLookup = 2
}

/// <summary>
/// Ambient operation intent for dependency telemetry. The scope flows with the asynchronous call so a storage or
/// API client can mark the HTTP request it is about to send without taking an OpenTelemetry dependency. Disposal
/// restores the previous intent, so nested and concurrent callers cannot observe each other's classification.
/// </summary>
public static class ExpectedDependency
{
    /// <summary>Low-cardinality tag that names the dependency operation.</summary>
    public const string KindTag = "mehrak.dependency.kind";

    /// <summary>Low-cardinality tag added only when a negative response was classified as expected.</summary>
    public const string OutcomeTag = "mehrak.dependency.outcome";

    public const string ExpectedMissOutcome = "expected_miss";

    public const string ExistenceProbeKind = "existence_probe";

    public const string WikiContentKind = "wiki_content";

    private static readonly AsyncLocal<ExpectedDependencyKind> AmbientKind = new();

    /// <summary>The intent for the active asynchronous flow.</summary>
    public static ExpectedDependencyKind Current => AmbientKind.Value;

    /// <summary>
    /// Marks dependency calls started in the current asynchronous flow until the returned scope is disposed.
    /// </summary>
    public static IDisposable Begin(ExpectedDependencyKind kind)
    {
        var previous = AmbientKind.Value;
        AmbientKind.Value = kind;
        return new Scope(previous);
    }

    /// <summary>Returns the stable tag value for an intent, or <see langword="null"/> when it is not classified.</summary>
    public static string? KindValue(ExpectedDependencyKind kind)
    {
        return kind switch
        {
            ExpectedDependencyKind.ExistenceProbe => ExistenceProbeKind,
            ExpectedDependencyKind.WikiContentLookup => WikiContentKind,
            _ => null
        };
    }

    private sealed class Scope(ExpectedDependencyKind previous) : IDisposable
    {
        private readonly ExpectedDependencyKind m_Previous = previous;
        private bool m_Disposed;

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            AmbientKind.Value = m_Previous;
        }
    }
}
