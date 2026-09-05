using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mehrak.Dashboard.Tests.Auth;

/// <summary>
/// Finding 13: only explicitly configured proxies may supply forwarded
/// headers; untrusted values are ignored while legitimate proxies keep
/// correct client-IP and HTTPS-scheme behavior.
/// </summary>
[TestFixture]
public class ForwardedHeadersConfigTests
{
    private static ForwardedHeadersOptions ConfigureWith(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var options = new ForwardedHeadersOptions();
        Program.ConfigureForwardedHeaders(options, configuration);
        return options;
    }

    [Test]
    public void ConfigureForwardedHeaders_SetsHeadersAndConstrainsDepth()
    {
        var options = ConfigureWith(new Dictionary<string, string?>());

        Assert.That(options.ForwardedHeaders,
            Is.EqualTo(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto));
        Assert.That(options.ForwardLimit, Is.EqualTo(1));
    }

    [Test]
    public void ConfigureForwardedHeaders_EmptyConfiguration_FallsBackToLoopbackOnly()
    {
        // An empty allowlist would trust every peer, so the fallback keeps
        // remote spoofing impossible while matching the framework default.
        var options = ConfigureWith(new Dictionary<string, string?>());

        Assert.That(options.KnownProxies.Select(p => p.ToString()),
            Is.EquivalentTo(["127.0.0.1", "::1"]));
        Assert.That(options.KnownIPNetworks, Is.Empty);
    }

    [Test]
    public void SplitProxyList_ParsesMixedSeparators()
    {
        var entries = Program.SplitProxyList("10.0.0.1, 10.0.0.2;10.0.0.3 10.0.0.4\t10.0.0.5");

        Assert.That(entries, Is.EquivalentTo(["10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4", "10.0.0.5"]));
    }

    [Test]
    public void SplitProxyList_NullOrBlank_ReturnsEmpty()
    {
        Assert.That(Program.SplitProxyList(null), Is.Empty);
        Assert.That(Program.SplitProxyList("   "), Is.Empty);
    }

    [Test]
    public void ConfigureForwardedHeaders_ParsesProxyIpsAndNetworks()
    {
        var options = ConfigureWith(new Dictionary<string, string?>
        {
            { "Nginx:KnownProxy", "10.0.0.1, 2001:db8::1" },
            { "Nginx:KnownNetworks", "10.1.0.0/16 2001:db8:1::/48" }
        });

        Assert.That(options.KnownProxies.Select(p => p.ToString()),
            Is.EquivalentTo(["10.0.0.1", "2001:db8::1"]));
        Assert.That(options.KnownIPNetworks.Select(n => n.ToString()),
            Is.EquivalentTo(["10.1.0.0/16", "2001:db8:1::/48"]));
    }

    [Test]
    public void ConfigureForwardedHeaders_IgnoresInvalidEntries()
    {
        var options = ConfigureWith(new Dictionary<string, string?>
        {
            { "Nginx:KnownProxy", "not-an-ip, 10.0.0.1, 999.999.0.1/33" }
        });

        Assert.That(options.KnownProxies.Select(p => p.ToString()), Is.EquivalentTo(["10.0.0.1"]));
        Assert.That(options.KnownIPNetworks, Is.Empty);
    }

    private static async Task<string> GetSeenClientAsync(
        IDictionary<string, string?> proxyConfig,
        string forwardedFor,
        string? forwardedProto = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(proxyConfig)
            .Build();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.Configure<ForwardedHeadersOptions>(
            options => Program.ConfigureForwardedHeaders(options, configuration));

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.MapGet("/whoami", (HttpContext context) =>
            $"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}");
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        await using (app)
        {
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
            if (forwardedProto is not null)
                request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync()).Trim();
        }
    }

    [Test]
    public async Task ForwardedHeaders_UntrustedPeerValuesAreIgnored()
    {
        var seen = await GetSeenClientAsync(
            new Dictionary<string, string?> { { "Nginx:KnownProxy", "10.0.0.1" } },
            "203.0.113.7");

        // The loopback test peer is not the configured proxy, so the
        // attacker-controlled header is ignored and the direct peer remains
        // the client IP.
        Assert.That(seen, Is.EqualTo("127.0.0.1|http"));
    }

    [Test]
    public async Task ForwardedHeaders_UntrustedNonLoopbackPeer_IsAlwaysIgnored()
    {
        // The real middleware, driven with a simulated untrusted peer:
        // forwarded values must never override the connection identity,
        // whether or not any proxy is configured.
        var configs = new IDictionary<string, string?>[]
        {
            new Dictionary<string, string?>(),
            new Dictionary<string, string?> { { "Nginx:KnownProxy", "10.0.0.1" } }
        };

        foreach (var proxyConfig in configs)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(proxyConfig)
                .Build();
            var options = new ForwardedHeadersOptions();
            Program.ConfigureForwardedHeaders(options, configuration);

            var invoked = false;
            RequestDelegate next = _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            };
            var middleware = new ForwardedHeadersMiddleware(
                next, NullLoggerFactory.Instance, Options.Create(options));

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            context.Request.Headers["X-Forwarded-Proto"] = "https";

            await middleware.Invoke(context);

            Assert.That(invoked, Is.True);
            Assert.That(context.Connection.RemoteIpAddress?.ToString(), Is.EqualTo("192.168.1.50"));
            // The forwarded proto was not applied (DefaultHttpContext leaves
            // Scheme empty unless something sets it).
            Assert.That(context.Request.Scheme, Is.Empty);
        }
    }

    [Test]
    public async Task ForwardedHeaders_TrustedProxyValuesAreHonored()
    {
        var seen = await GetSeenClientAsync(
            new Dictionary<string, string?> { { "Nginx:KnownProxy", "127.0.0.1" } },
            "203.0.113.7",
            "https");

        // A legitimate proxy still yields the real client IP and HTTPS scheme
        // required by OAuth handling.
        Assert.That(seen, Is.EqualTo("203.0.113.7|https"));
    }
}
