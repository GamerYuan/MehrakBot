#region

using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Mehrak.Application.Tests.TestUtils;

#endregion

namespace Mehrak.Application.Tests.Benchmarks;

/// <summary>
/// Explicit, test-only harness for measuring the existing golden-image card
/// cases. Select this test by its exact fully-qualified name; normal test runs
/// never execute it.
/// </summary>
[TestFixture]
[Explicit("Run only for controlled paired card benchmarks")]
[NonParallelizable]
public sealed class CardFixtureBenchmarkTests
{
    internal const string FilterEnvironmentVariable = "MEHRAK_CARD_BENCHMARK_FILTER";
    internal const string LegacyBackgroundsEnvironmentVariable = "MEHRAK_CARD_BENCHMARK_LEGACY_BACKGROUNDS";
    internal const int ExpectedCaseCount = 73;

    [Test]
    public async Task RunGoldenCardFixtures_WithWarmup_RecordsMeasuredSamples()
    {
        var outputPath = Environment.GetEnvironmentVariable("MEHRAK_CARD_BENCHMARK_OUTPUT");
        Assert.That(string.IsNullOrWhiteSpace(outputPath), Is.False,
            "MEHRAK_CARD_BENCHMARK_OUTPUT must name a new JSONL output file");

        outputPath = Path.GetFullPath(outputPath!);
        Assert.That(File.Exists(outputPath), Is.False,
            $"Benchmark output already exists and would mix samples: {outputPath}");
        Assert.That(Directory.Exists(Path.GetDirectoryName(outputPath)), Is.True,
            "The benchmark output directory must already exist");

        var allCases = CardFixtureBenchmarkRunner.DiscoverGoldenCases(GetType().Assembly);
        Assert.That(allCases, Has.Count.EqualTo(ExpectedCaseCount),
            "Golden fixture discovery changed; review the benchmark corpus before changing the expected count");
        Assert.That(allCases.Select(testCase => testCase.Identity), Is.Unique,
            "Every benchmark case must have a unique identity");

        var filter = Environment.GetEnvironmentVariable(FilterEnvironmentVariable);
        var filters = filter?.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var selectedCases = string.IsNullOrWhiteSpace(filter)
            ? allCases
            : allCases.Where(testCase => filters.Any(item =>
                testCase.Identity.Contains(item, StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.That(selectedCases, Is.Not.Empty,
            $"No benchmark case identity contains filter '{filter}'");

        var useLegacyBackgrounds = string.Equals(
            Environment.GetEnvironmentVariable(LegacyBackgroundsEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

        foreach (var testCase in selectedCases)
        {
            using (CardBenchmarkMetrics.BeginScope(
                       testCase.Identity,
                       suppressSamples: true,
                       useLegacyBackgrounds: useLegacyBackgrounds))
                await CardFixtureBenchmarkRunner.InvokeCaseAsync(testCase);

            using (CardBenchmarkMetrics.BeginScope(
                       testCase.Identity,
                       useLegacyBackgrounds: useLegacyBackgrounds))
                await CardFixtureBenchmarkRunner.InvokeCaseAsync(testCase);
        }

        var recordedIdentities = File.ReadLines(outputPath)
            .Select(ReadTestIdentity)
            .ToArray();
        Assert.That(recordedIdentities, Is.EqualTo(selectedCases.Select(testCase => testCase.Identity)),
            "Each selected case must produce exactly one measured sample in deterministic order");
    }

    private static string ReadTestIdentity(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("test").GetString()
               ?? throw new InvalidDataException("Benchmark sample has a null test identity");
    }
}

internal static class CardFixtureBenchmarkRunner
{
    private const string MatchesGoldenImage = "MatchesGoldenImage";
    private const string ShouldMatchGoldenImage = "ShouldMatchGoldenImage";

    internal static IReadOnlyList<CardFixtureBenchmarkCase> DiscoverGoldenCases(Assembly assembly) =>
        DiscoverGoldenCases(assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.Name.EndsWith("CardServiceTests", StringComparison.Ordinal)));

    internal static IReadOnlyList<CardFixtureBenchmarkCase> DiscoverGoldenCases(IEnumerable<Type> fixtureTypes)
    {
        var cases = fixtureTypes
            .Where(type => !type.IsDefined(typeof(ExplicitAttribute), true))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsGoldenTestMethod)
                .SelectMany(method => method.GetCustomAttributes<TestCaseAttribute>(false)
                    .Where(testCase => !testCase.Explicit)
                    .Select(testCase => CreateCase(type, method, testCase.Arguments))))
            .OrderBy(testCase => testCase.Identity, StringComparer.Ordinal)
            .ToArray();

        return cases;
    }

    internal static async Task InvokeCaseAsync(CardFixtureBenchmarkCase testCase)
    {
        object fixture;
        try
        {
            fixture = Activator.CreateInstance(testCase.FixtureType, true)
                      ?? throw new InvalidOperationException($"Could not create {testCase.FixtureType.FullName}");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();
        var oneTimeSetUpCompleted = false;
        var setUpCompleted = false;

        try
        {
            await InvokeLifecycleAsync(fixture, typeof(OneTimeSetUpAttribute), true);
            oneTimeSetUpCompleted = true;
            await InvokeLifecycleAsync(fixture, typeof(SetUpAttribute), true);
            setUpCompleted = true;
            await InvokeMethodAsync(fixture, testCase.Method, testCase.Arguments);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            if (setUpCompleted)
                await CaptureCleanupFailureAsync(
                    () => InvokeLifecycleAsync(fixture, typeof(TearDownAttribute), false), cleanupFailures);
            if (oneTimeSetUpCompleted)
                await CaptureCleanupFailureAsync(
                    () => InvokeLifecycleAsync(fixture, typeof(OneTimeTearDownAttribute), false), cleanupFailures);

            await CaptureFixtureDisposalFailureAsync(fixture, cleanupFailures);
        }

        ThrowFailures(primaryFailure, cleanupFailures);
    }

    private static bool IsGoldenTestMethod(MethodInfo method) =>
        !method.IsDefined(typeof(ExplicitAttribute), true) &&
        (method.Name.Contains(MatchesGoldenImage, StringComparison.Ordinal) ||
         method.Name.Contains(ShouldMatchGoldenImage, StringComparison.Ordinal));

    private static CardFixtureBenchmarkCase CreateCase(Type fixtureType, MethodInfo method, object?[] arguments)
    {
        if (method.GetParameters().Length != arguments.Length)
            throw new InvalidOperationException($"Benchmark case argument mismatch for {fixtureType.FullName}.{method.Name}");

        var identity = $"{fixtureType.FullName}.{method.Name}({string.Join(",", arguments.Select(FormatArgument))})";
        return new CardFixtureBenchmarkCase(fixtureType, method, arguments, identity);
    }

    private static string FormatArgument(object? argument) => argument switch
    {
        null => "null",
        string value => JsonSerializer.Serialize(value),
        char value => JsonSerializer.Serialize(value),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(argument, argument.GetType())
    };

    private static async Task InvokeLifecycleAsync(object fixture, Type attributeType, bool baseFirst)
    {
        foreach (var method in GetLifecycleMethods(fixture.GetType(), attributeType, baseFirst))
            await InvokeMethodAsync(fixture, method, []);
    }

    private static IEnumerable<MethodInfo> GetLifecycleMethods(Type fixtureType, Type attributeType, bool baseFirst)
    {
        var hierarchy = new Stack<Type>();
        for (var type = fixtureType; type != null && type != typeof(object); type = type.BaseType)
            hierarchy.Push(type);

        var types = hierarchy.ToArray();
        if (!baseFirst)
            Array.Reverse(types);

        return types.SelectMany(type => type
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes(attributeType, false).Length != 0)
            .OrderBy(method => method.MetadataToken));
    }

    private static async Task InvokeMethodAsync(object fixture, MethodInfo method, object?[] arguments)
    {
        object? result;
        try
        {
            result = method.Invoke(fixture, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        switch (result)
        {
            case Task task:
                await task;
                break;
            case ValueTask valueTask:
                await valueTask;
                break;
        }
    }

    private static async Task CaptureCleanupFailureAsync(Func<Task> cleanup, ICollection<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task CaptureFixtureDisposalFailureAsync(object fixture, ICollection<Exception> failures)
    {
        try
        {
            switch (fixture)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void ThrowFailures(Exception? primaryFailure, IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (primaryFailure is null && cleanupFailures.Count == 0)
            return;

        if (primaryFailure is not null && cleanupFailures.Count == 0)
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();

        var failures = primaryFailure is null
            ? cleanupFailures
            : new[] { primaryFailure }.Concat(cleanupFailures);
        throw new AggregateException("Card benchmark fixture invocation failed", failures);
    }
}

internal sealed record CardFixtureBenchmarkCase(
    Type FixtureType,
    MethodInfo Method,
    object?[] Arguments,
    string Identity);
