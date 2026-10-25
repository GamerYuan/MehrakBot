using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Modules;
using Mehrak.Bot.Shared.Services;
using Mehrak.Bot.Shared.Services.RateLimit;
using Mehrak.Bot.Tests.TestUtils;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.Infrastructure.Shared;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Extensions;
using Mehrak.Infrastructure.User.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Tests.Shared.Localization;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class CommandUiRuntimeTests
{
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    [TestCase("zh-CN")]
    [TestCase("zh-TW")]
    [TestCase("ja-JP")]
    [TestCase("ko-KR")]
    [TestCase("de-DE")]
    [TestCase("fr-FR")]
    [TestCase("pt-BR")]
    public void LocalisedAuthModals_KeepCanonicalIdsAndPassphraseSecurityBounds(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var add = AuthModalModule.AddAuthModalFor(culture);
        var update = AuthModalModule.UpdateAuthModal(new UserProfileDto { ProfileId = 2, LtUid = 123 }, culture);
        var unlock = AuthModalModule.AuthModal("synthetic-guid", culture);

        Assert.Multiple(() =>
        {
            Assert.That(add.CustomId, Is.EqualTo("add_auth_modal"));
            Assert.That(update.CustomId, Is.EqualTo("update_auth_modal:2"));
            Assert.That(unlock.CustomId, Is.EqualTo("auth_modal:synthetic-guid"));
        });
        foreach (var modal in new[] { add, update, unlock })
        {
            Assert.That(modal.Title, Has.Length.InRange(1, 45));
            Assert.That(modal.Title, Is.Not.EqualTo(modal == update ? "Update Authentication" : "Authenticate"));
            var labels = modal.Components.OfType<LabelProperties>().ToArray();
            foreach (var label in labels)
            {
                Assert.That(label.Label, Has.Length.InRange(1, 45));
                var input = (TextInputProperties)label.Component;
                Assert.That(input.Placeholder, Has.Length.InRange(1, 100));
                Assert.That(input.CustomId, Is.EqualTo("ltoken").Or.EqualTo("passphrase"));
            }
            var passphrase = labels.Select(x => x.Component).OfType<TextInputProperties>()
                .Single(x => x.CustomId == "passphrase");
            Assert.That(passphrase.MaxLength, Is.EqualTo(64));
            Assert.That(passphrase.MinLength, Is.EqualTo(modal == unlock ? (int?)null : 12));
        }
    }

    [Test]
    public async Task ProfileList_LocalisesOnlyUiAndRemainsPrivate()
    {
        var profile = new UserProfileModel
        {
            ProfileId = 1, LtUid = 123, LToken = "synthetic-credential-never-display",
            GameUids = [new ProfileGameUid { Game = Game.Genshin, Region = "os_asia", GameUid = "800000001" }]
        };
        using var database = new TestDbContextFactory(seed: db => db.Users.Add(new UserModel
        {
            Id = 42, Profiles = [profile]
        }));
        using var scope = database.ScopeFactory.CreateScope();
        using var discord = new DiscordTestHelper();
        var context = new ApplicationCommandContext(discord.CreateCommandInteraction(42, userLocale: "ja"), discord.DiscordClient);
        var module = new ProfileCommandModule(scope.ServiceProvider.GetRequiredService<UserDbContext>(),
            NullLogger<ProfileCommandModule>.Instance);
        SetContext(module, context);

        await module.ListProfileCommand();
        using var payload = await ReadPayload(discord);
        Assert.Multiple(() =>
        {
            Assert.That(Text(payload.RootElement), Does.Contain("プロフィール 1").And.Contain("ゲーム：")
                .And.Contain("Genshin Impact").And.Contain("os_asia: 800000001")
                .And.Not.Contain(profile.LToken));
            Assert.That(Flags(payload.RootElement), Is.EqualTo(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
            Assert.That(ProfileTextRenderer.Render(profile, CommandUiLocalizer.Default, BotUiCulture.English),
                Is.EqualTo(profile.ToDisplayString()), "Neutral English must retain the shared profile layout");
        });
    }

    [Test]
    public async Task AuthenticationNotFound_UsesTheInvokingLocale()
    {
        using var database = new TestDbContextFactory();
        using var discord = new DiscordTestHelper();
        var context = new ApplicationCommandContext(discord.CreateCommandInteraction(42, userLocale: "ja"), discord.DiscordClient);
        var service = new AuthenticationMiddlewareService(Mock.Of<ICacheService>(), Mock.Of<IEncryptionService>(),
            database.ScopeFactory, NullLogger<AuthenticationMiddlewareService>.Instance, Mock.Of<IPassphraseAttemptRateLimiter>());

        var result = await service.GetAuthenticationAsync(new AuthenticationRequest(context, 1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AuthStatus.NotFound));
            Assert.That(result.ErrorMessage, Is.EqualTo("ユーザーアカウントが見つかりません。先にプロフィールを追加してください。"));
            Assert.That(discord.CapturedRequests, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RateLimit_RejectionAndServiceFailureAreLocalised(bool unavailable)
    {
        using var discord = new DiscordTestHelper();
        using var services = new ServiceCollection().AddSingleton(Mock.Of<ICommandRateLimitService>(
            x => x.IsAllowedAsync(It.IsAny<ulong>()) == Task.FromResult(false))).BuildServiceProvider();
        var context = new ApplicationCommandContext(discord.CreateCommandInteraction(42, userLocale: "ja"), discord.DiscordClient);

        var result = await new RateLimitAttribute<ApplicationCommandContext>().EnsureCanExecuteAsync(context,
            unavailable ? null : services);

        var expectedKey = unavailable ? CommandUiKeys.RateLimitUnavailable : CommandUiKeys.RateLimitTooFrequent;
        Assert.That(((IFailResult)result).Message, Is.EqualTo(CommandUiLocalizer.Default.Get(Japanese, expectedKey)));
    }

    [TestCase("input")]
    [TestCase("exception")]
    [TestCase("untrusted-precondition")]
    [TestCase("rate-limit")]
    public async Task FrameworkFailures_AreLocalisedPrivateAndNeverEchoUnsafeText(string kind)
    {
        using var discord = new DiscordTestHelper();
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new ApplicationCommandContext(discord.CreateCommandInteraction(42, userLocale: "ja"), discord.DiscordClient);
        const string unsafeText = "SYNTHETIC-SECRET-MUST-NOT-LEAK";
        var rateLimit = CommandUiLocalizer.Default.Get(Japanese, CommandUiKeys.RateLimitTooFrequent);
        IExecutionResult result = kind switch
        {
            "exception" => new SyntheticException(unsafeText, new InvalidOperationException(unsafeText)),
            "untrusted-precondition" => new PreconditionFailResult(unsafeText),
            "rate-limit" => new PreconditionFailResult(rateLimit),
            _ => new SlashCommandTypeReaderFailResult(unsafeText)
        };

        await new CustomCommandResultHandler<ApplicationCommandContext>().HandleResultAsync(result, context,
            discord.DiscordClient, NullLogger.Instance, services);

        using var payload = await ReadPayload(discord);
        var expected = kind == "rate-limit" ? rateLimit : CommandUiLocalizer.Default.Get(Japanese,
            kind == "exception" ? CommandUiKeys.FrameworkUnexpectedError : CommandUiKeys.FrameworkInputInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(Text(payload.RootElement), Is.EqualTo(expected));
            Assert.That(Text(payload.RootElement), Does.Not.Contain(unsafeText));
            Assert.That(Flags(payload.RootElement), Is.EqualTo(MessageFlags.Ephemeral));
        });
    }

    [Test]
    public async Task FrameworkFailure_AfterAnInitialResponseStillSendsLocalisedPrivateFollowup()
    {
        using var discord = new DiscordTestHelper();
        discord.SetupRequestCapture();
        using var services = new ServiceCollection().BuildServiceProvider();
        var interaction = new SlashCommandInteraction(new JsonInteraction
        {
            ApplicationId = 123UL, Token = "synthetic-token", UserLocale = "ja", Entitlements = [],
            User = new JsonUser { Id = 42 },
            Channel = new JsonChannel { Id = 321, Type = ChannelType.TextGuildChannel },
            Data = new JsonInteractionData { Name = "test", Type = ApplicationCommandType.ChatInput, Options = [] }
        }, null, (_, _, _, _, _) => throw new InvalidOperationException("Already responded"), discord.DiscordClient.Rest);
        var context = new ApplicationCommandContext(interaction, discord.DiscordClient);

        await new CustomCommandResultHandler<ApplicationCommandContext>().HandleResultAsync(
            new SlashCommandTypeReaderFailResult("untrusted-framework-text"), context,
            discord.DiscordClient, NullLogger.Instance, services);

        using var payload = await ReadPayload(discord);
        Assert.Multiple(() =>
        {
            Assert.That(Text(payload.RootElement), Is.EqualTo(CommandUiLocalizer.Default.Get(Japanese,
                CommandUiKeys.FrameworkInputInvalid)));
            Assert.That(Flags(payload.RootElement), Is.EqualTo(MessageFlags.Ephemeral));
            Assert.That(discord.CapturedRequests.Single().Request.RequestUri!.AbsolutePath,
                Does.Contain("webhooks"));
        });
    }

    [Test]
    public void Ping_UsesUserLocaleButKeepsTheEnglishFallback()
    {
        using var discord = new DiscordTestHelper();
        var module = new PingCommandModule();
        SetContext(module, new ApplicationCommandContext(
            discord.CreateCommandInteraction(42, userLocale: "ja"), discord.DiscordClient));
        Assert.That(module.Ping(), Is.EqualTo("応答あり！"));
        SetContext(module, new ApplicationCommandContext(
            discord.CreateCommandInteraction(42, userLocale: "es-ES"), discord.DiscordClient));
        Assert.That(module.Ping(), Is.EqualTo("Pong"));
    }

    private static void SetContext(ApplicationCommandModule<ApplicationCommandContext> module,
        ApplicationCommandContext context)
    {
        // NetCord injects this field through an internal interface during command dispatch.
        typeof(ApplicationCommandModule<ApplicationCommandContext>).BaseType!
            .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, context);
    }

    private static async Task<JsonDocument> ReadPayload(DiscordTestHelper discord)
    {
        var body = await discord.CapturedRequests.Last().Content!.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }

    private static MessageFlags Flags(JsonElement payload) =>
        (MessageFlags)(payload.TryGetProperty("data", out var data) ? data : payload).GetProperty("flags").GetInt32();

    private static string Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => string.Join("", element.EnumerateObject().Select(x =>
            x.Name == "content" ? x.Value.GetString() : Text(x.Value))),
        JsonValueKind.Array => string.Join("", element.EnumerateArray().Select(Text)),
        _ => string.Empty
    };

    private sealed record SyntheticException(string Message, Exception Exception) : IFailResult, IExceptionResult;
}
