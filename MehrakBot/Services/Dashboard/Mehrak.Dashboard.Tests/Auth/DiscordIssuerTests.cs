using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using static OpenIddict.Client.OpenIddictClientEvents;

namespace Mehrak.Dashboard.Tests.Auth;

[TestFixture]
public class DiscordIssuerTests
{
    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenIddict().AddClient(options =>
        {
            options.AllowAuthorizationCodeFlow();
            options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            options.UseSystemNetHttp();
            options.UseWebProviders().AddDiscord(discord => discord
                .SetClientId("test-client")
                .SetClientSecret("test-secret")
                .SetRedirectUri("https://dashboard.example/auth/callback"));
        });
        services.PostConfigure<OpenIddictClientOptions>(Program.ConfigureDiscordIssuerSupport);
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task DiscordConfiguration_AdvertisesIssuerSupportAndPreservesProviderDefaults()
    {
        using var services = CreateServices();
        var options = services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        var registration = options.Registrations.Single();
        var configuration = await registration.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.AuthorizationResponseIssParameterSupported, Is.True);
            Assert.That(registration.Issuer, Is.EqualTo(new Uri("https://discord.com")));
            Assert.That(configuration.AuthorizationEndpoint, Is.EqualTo(new Uri("https://discord.com/oauth2/authorize")));
            Assert.That(configuration.TokenEndpoint, Is.EqualTo(new Uri("https://discord.com/api/oauth2/token")));
            Assert.That(configuration.UserInfoEndpoint, Is.EqualTo(new Uri("https://discord.com/api/users/@me")));
            Assert.That(configuration.CodeChallengeMethodsSupported, Does.Contain("S256"));
            Assert.That(registration.Scopes, Does.Contain("identify"));
        }
    }

    [TestCase("https://discord.com", false)]
    [TestCase("https://attacker.example", true)]
    [TestCase("http://discord.com", true)]
    [TestCase("https://discord.com/other", true)]
    [TestCase("not-a-uri", true)]
    [TestCase(null, true)]
    [TestCase("", true)]
    public async Task CallbackIssuer_UsesBuiltInValidation(string? issuer, bool rejected)
    {
        using var services = CreateServices();
        var options = services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        var registration = options.Registrations.Single();
        var context = new ProcessAuthenticationContext(new OpenIddictClientTransaction())
        {
            EndpointType = OpenIddictClientEndpointType.Redirection,
            Registration = registration,
            Configuration = await registration.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None),
            Request = new OpenIddictRequest { [OpenIddictConstants.Parameters.Iss] = issuer }
        };

        await new OpenIddictClientHandlers.ValidateIssuerParameter().HandleAsync(context);

        Assert.That(context.IsRejected, Is.EqualTo(rejected));
        if (rejected)
            Assert.That(context.Error, Is.EqualTo(OpenIddictConstants.Errors.InvalidRequest));
    }

    [Test]
    public void ConfigureDiscordIssuerSupport_DoesNotChangeOtherProviders()
    {
        var configuration = new OpenIddictConfiguration { AuthorizationResponseIssParameterSupported = false };
        var options = new OpenIddictClientOptions();
        options.Registrations.Add(new OpenIddictClientRegistration
        {
            ProviderType = "another-provider",
            Configuration = configuration
        });

        Program.ConfigureDiscordIssuerSupport(options);

        Assert.That(configuration.AuthorizationResponseIssParameterSupported, Is.False);
    }
}
