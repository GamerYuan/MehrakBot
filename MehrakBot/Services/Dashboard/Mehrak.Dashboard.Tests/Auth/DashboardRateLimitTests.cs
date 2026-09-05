using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Mehrak.Dashboard.Auth;
using Mehrak.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Options;

namespace Mehrak.Dashboard.Tests.Auth;

/// <summary>
/// Finding 10: the IP rate limiter runs before authentication, session
/// validation is read-only, and the strict login policy guards auth endpoints.
/// </summary>
[TestFixture]
public class DashboardRateLimitTests
{
    /// <summary>
    /// Stands in for the database-backed session validation: every invocation
    /// represents session storage work on the authentication hot path.
    /// </summary>
    private sealed class StubSessionWriteHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public static int SessionWrites;

        public StubSessionWriteHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            Interlocked.Increment(ref SessionWrites);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateAppAsync(
        Action<WebApplication> map,
        Action<WebApplicationBuilder>? configure = null)
    {
        StubSessionWriteHandler.SessionWrites = 0;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddRateLimiter(Program.ConfigureRateLimiter);
        builder.Services.AddAuthentication("Stub")
            .AddScheme<AuthenticationSchemeOptions, StubSessionWriteHandler>("Stub", _ => { })
            .AddScheme<AuthenticationSchemeOptions, StubSessionWriteHandler>("Discord", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
        configure?.Invoke(builder);

        var app = builder.Build();
        Program.UseDashboardMiddleware(app);
        map(app);
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    private static async Task<(int Allowed, int Rejected)> GetCountsAsync(HttpClient client, string path, int total)
    {
        var allowed = 0;
        var rejected = 0;
        for (var i = 0; i < total; i++)
        {
            using var response = await client.GetAsync(path);
            if ((int)response.StatusCode == StatusCodes.Status429TooManyRequests)
                rejected++;
            else if (response.IsSuccessStatusCode
                || response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Redirect)
                allowed++;
            else
                Assert.Fail($"Unexpected status code {response.StatusCode}");
        }
        return (allowed, rejected);
    }

    [Test]
    public async Task OverLimitRequests_SkipSessionStorage()
    {
        var (app, client) = await CreateAppAsync(endpoints => endpoints.MapGet("/probe", () => Results.Ok()));
        await using (app)
        {
            using (client)
            {
                var (allowed, rejected) = await GetCountsAsync(client, "/probe", 105);

                Assert.That(allowed, Is.EqualTo(100));
                Assert.That(rejected, Is.EqualTo(5));
                // Rejected requests never reached authentication: no session work.
                Assert.That(StubSessionWriteHandler.SessionWrites, Is.EqualTo(100));
            }
        }
    }

    [Test]
    public async Task LoginPolicy_EnforcesStrictLimit()
    {
        var (app, client) = await CreateAppAsync(endpoints =>
            endpoints.MapGet("/auth-probe", () => Results.Ok()).RequireRateLimiting("login"));
        await using (app)
        {
            using (client)
            {
                var (allowed, rejected) = await GetCountsAsync(client, "/auth-probe", 7);

                Assert.That(allowed, Is.EqualTo(5));
                Assert.That(rejected, Is.EqualTo(2));
            }
        }
    }

    [Test]
    public async Task AuthEndpoints_EnforceStrictLoginPolicyEndToEnd()
    {
        // Exercises the real production wiring: real middleware order, real
        // limiter configuration, and the real controller mapping with the
        // login policy pinned onto the auth endpoints.
        var (app, client) = await CreateAppAsync(
            static app => app.MapControllers(),
            builder =>
            {
                builder.Services.AddSingleton(Mock.Of<IDashboardAuthService>());
                builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                builder.Services.AddControllers(Program.ConfigureMvc)
                    .AddApplicationPart(typeof(AuthController).Assembly);
            });
        await using (app)
        {
            using (client)
            {
                var (allowed, rejected) = await GetCountsAsync(client, "/auth/discord", 7);

                Assert.That(allowed, Is.EqualTo(5));
                Assert.That(rejected, Is.EqualTo(2));
            }
        }
    }

    private static ApplicationModel CreateApplicationModel(Type controllerType, string methodName)
    {
        var application = new ApplicationModel();
        var controller = new ControllerModel(controllerType.GetTypeInfo(), []);
        var action = new ActionModel(controllerType.GetMethod(methodName)!, []);
        action.Controller = controller;
        action.Selectors.Add(new SelectorModel());
        controller.Actions.Add(action);
        application.Controllers.Add(controller);
        return application;
    }

    private static void ApplyRegisteredConventions(ApplicationModel application)
    {
        var mvc = new MvcOptions();
        Program.ConfigureMvc(mvc);
        Assert.That(mvc.Conventions, Has.Count.EqualTo(1));
        foreach (var convention in mvc.Conventions)
            convention.Apply(application);
    }

    private static IEnumerable<EnableRateLimitingAttribute> RateLimitMetadata(ApplicationModel application) =>
        application.Controllers
            .SelectMany(c => c.Actions)
            .SelectMany(a => a.Selectors)
            .SelectMany(s => s.EndpointMetadata)
            .OfType<EnableRateLimitingAttribute>();

    [Test]
    public void LoginRateLimitConvention_PinsStrictPolicyOntoEveryAuthAction()
    {
        foreach (var method in new[]
                 {
                     nameof(AuthController.Discord),
                     nameof(AuthController.DiscordCallback),
                     nameof(AuthController.Logout)
                 })
        {
            var application = CreateApplicationModel(typeof(AuthController), method);

            ApplyRegisteredConventions(application);

            var attribute = RateLimitMetadata(application).Single();
            Assert.That(attribute.PolicyName, Is.EqualTo("login"), $"action {method}");
        }
    }

    [Test]
    public void LoginRateLimitConvention_LeavesOtherControllersUntouched()
    {
        var application = CreateApplicationModel(
            typeof(Mehrak.Dashboard.Profile.ProfileController),
            nameof(Mehrak.Dashboard.Profile.ProfileController.ListProfiles));

        ApplyRegisteredConventions(application);

        Assert.That(RateLimitMetadata(application), Is.Empty);
    }
}
