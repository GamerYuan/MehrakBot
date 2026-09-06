using System.Diagnostics.Metrics;
using System.Reflection;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using OpenTelemetry.Metrics;

namespace Mehrak.Bot.Tests.Shared.Services;

[TestFixture]
public class BotMetricsServiceTests
{
    [Test]
    public async Task Latency_ReportsCurrentGatewayValueBeforeAnyCommandsExecute()
    {
        using var discord = new DiscordTestHelper();
        // NetCord exposes latency as read-only; simulate its heartbeat updates without connecting to Discord.
        var updateLatency = typeof(WebSocketClient).GetMethod("UpdateLatencyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (ValueTask)updateLatency.Invoke(discord.DiscordClient, [TimeSpan.FromMilliseconds(12.5)])!;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(discord.DiscordClient);
        services.AddBotServices();
        using var provider = services.BuildServiceProvider();
        using var listener = new MeterListener();
        List<double> measurements = [];
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "MehrakBot" && instrument.Name == "bot_latency_ms")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => measurements.Add(value));
        listener.Start();

        // Starting telemetry must create the gauge without a command resolving IBotMetrics first.
        _ = provider.GetRequiredService<MeterProvider>();
        listener.RecordObservableInstruments();
        await (ValueTask)updateLatency.Invoke(discord.DiscordClient, [TimeSpan.FromMilliseconds(42.25)])!;
        listener.RecordObservableInstruments();

        Assert.That(measurements, Is.EqualTo(new[] { 12.5, 42.25 }));
        Assert.That(provider.GetServices<IBotMetrics>(), Has.Exactly(1).Items);
    }
}
