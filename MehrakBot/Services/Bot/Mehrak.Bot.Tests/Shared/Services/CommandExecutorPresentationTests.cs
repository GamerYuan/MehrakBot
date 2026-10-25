using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Mehrak.Bot.Genshin;
using Mehrak.Bot.Hi3;
using Mehrak.Bot.Hsr;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Builders;
using Mehrak.Bot.Shared.Services;
using Mehrak.Bot.Tests.TestUtils;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using ApplicationService = Mehrak.Domain.Protobuf.ApplicationService;
using ExecuteRequest = Mehrak.Domain.Protobuf.ExecuteRequest;
using Result = Mehrak.Domain.Command.Models.CommandResult;

namespace Mehrak.Bot.Tests.Shared.Services;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CommandExecutorPresentationTests
{
    private readonly Mock<ApplicationService.ApplicationServiceClient> m_Application = new();
    private readonly Mock<IAuthenticationMiddlewareService> m_Auth = new();
    private readonly Mock<IBotMetrics> m_Metrics = new();
    private readonly Mock<IAttachmentStorageService> m_Storage = new();
    private readonly Mock<IImageRepository> m_Images = new();
    private readonly List<string> m_Messages = [];
    private readonly List<TrackingStream> m_Streams = [];
    private TestDbContextFactory m_Factory = null!;
    private IServiceScope m_Scope = null!;
    private GatewayClient m_Discord = null!;
    private CommandExecutorService m_Executor = null!;
    private bool m_ThrowOnCardSend;

    [SetUp]
    public void SetUp()
    {
        m_Factory = new TestDbContextFactory();
        m_Scope = m_Factory.ScopeFactory.CreateScope();
        var handler = new Mock<IRestRequestHandler>();
        handler.Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, CancellationToken>(async (request, ct) =>
            {
                if (request.Content?.Headers.ContentType?.MediaType == "multipart/form-data")
                    Assert.That(m_Streams.Select(x => x.IsDisposed), Is.All.False, "Streams must stay alive until the card send completes");
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                m_Messages.Add(body);
                if (m_ThrowOnCardSend && body.Contains("remove_card", StringComparison.Ordinal))
                    throw new HttpRequestException("Synthetic Discord send failure");
                return new HttpResponseMessage
                {
                    Content = JsonContent.Create(new JsonMessage { Author = new JsonUser(), MentionedUsers = [], Attachments = [], Embeds = [] })
                };
            });
        m_Discord = new GatewayClient(Mock.Of<IEntityToken>(), new GatewayClientConfiguration
        {
            RestClientConfiguration = new() { RequestHandler = handler.Object }
        });
        var interaction = new SlashCommandInteraction(new JsonInteraction
        {
            ApplicationId = 123UL, Type = InteractionType.ApplicationCommand, AppPermissions = Permissions.SendMessages,
            Token = "synthetic-token", User = new JsonUser { Id = 42 }, Entitlements = [],
            Channel = new JsonChannel { Id = 321, Type = ChannelType.TextGuildChannel },
            Data = new JsonInteractionData { Id = 1, Name = "test", Type = ApplicationCommandType.ChatInput, Options = [] }
        }, null, async (i, callback, _, _, ct) =>
            await m_Discord.Rest.SendInteractionResponseAsync(i.Id, i.Token, callback, false, null, ct), m_Discord.Rest);
        var context = new Mock<IInteractionContext>();
        context.SetupGet(x => x.Interaction).Returns(interaction);
        m_Auth.Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>())).ReturnsAsync(
            AuthenticationResult.Success(42, 123, "synthetic-ltoken", new UserDto { Id = 42, Profiles = [] }, context.Object));
        m_Metrics.Setup(x => x.ObserveCommandDuration(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());
        m_Executor = new(m_Scope.ServiceProvider.GetRequiredService<UserDbContext>(), m_Auth.Object, m_Metrics.Object,
            m_Application.Object, m_Storage.Object, m_Images.Object, NullLogger<CommandExecutorService>.Instance)
        {
            Context = context.Object, CommandName = "test", ValidateServer = false
        };
    }

    [TearDown]
    public void TearDown()
    {
        m_Discord.Dispose();
        m_Scope.Dispose();
        m_Factory.Dispose();
        foreach (var stream in m_Streams) stream.Dispose();
    }

    private void Return(Result result) => ReturnWire(result.ToProto());
    private void ReturnWire(Mehrak.Domain.Protobuf.CommandResult result) =>
        m_Application.Setup(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default))
            .Returns(new AsyncUnaryCall<Mehrak.Domain.Protobuf.CommandResult>(Task.FromResult(result), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => [], () => { }));

    private TrackingStream StoreStream(string key)
    {
        var stream = new TrackingStream();
        m_Streams.Add(stream);
        m_Storage.Setup(x => x.DownloadAsync(key, default)).ReturnsAsync(new AttachmentDownloadResult(stream, "image/png"));
        return stream;
    }

    private void UseLocale(string locale)
    {
        var interaction = new SlashCommandInteraction(new JsonInteraction
        {
            ApplicationId = 123UL, Type = InteractionType.ApplicationCommand, AppPermissions = Permissions.SendMessages,
            Token = "synthetic-token", User = new JsonUser { Id = 42 }, Entitlements = [], UserLocale = locale,
            Channel = new JsonChannel { Id = 321, Type = ChannelType.TextGuildChannel },
            Data = new JsonInteractionData { Id = 1, Name = "test", Type = ApplicationCommandType.ChatInput, Options = [] }
        }, null, async (i, callback, _, _, ct) =>
            await m_Discord.Rest.SendInteractionResponseAsync(i.Id, i.Token, callback, false, null, ct), m_Discord.Rest);
        var context = new Mock<IInteractionContext>();
        context.SetupGet(x => x.Interaction).Returns(interaction);
        m_Executor.Context = context.Object;
        m_Auth.Setup(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>())).ReturnsAsync(
            AuthenticationResult.Success(42, 123, "synthetic-ltoken", new UserDto { Id = 42, Profiles = [] }, context.Object));
    }

    [Test]
    public async Task LocalisedPartialCard_KeepsPrivateErrorsPublicAttachmentsAndCanonicalRemoveId()
    {
        UseLocale("ja");
        Return(Result.Success(CharacterCardsOutcome.Create(Game.Genshin, "uid",
        [
            CharacterCardItem.Generated("Nahida", "Nahida", 1, StorageReference.Attachment("canonical-hash.png")),
            CharacterCardItem.NotFound("MissingEnglishName")
        ], OperationOutcome.Partial)));

        await m_Executor.ExecuteAsync(1);

        var messages = m_Messages.Select(DecodedStrings).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Length.EqualTo(2));
            Assert.That(messages[0], Does.Contain("キャラクター MissingEnglishName が見つかりません"));
            Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.Ephemeral));
            Assert.That(messages[1], Does.Contain("attachment://canonical-hash.png").And.Contain("remove_card")
                .And.Contain("削除").And.Not.Contain("MissingEnglishName"));
            Assert.That(Flags(m_Messages[1]), Is.EqualTo(MessageFlags.IsComponentsV2));
        });
    }

    [Test]
    public async Task LocalisedValidation_KeepsCanonicalFloorNameAndNeverAuthenticatesInvalidInput()
    {
        UseLocale("ja");
        m_Executor.Parameters["floor"] = 8;
        m_Executor.AddValidator<int>("floor", floor => floor >= 9 && floor <= 12,
            Mehrak.Bot.Shared.Localization.CommandUiKeys.ExecutorValidationFloor);

        await m_Executor.ExecuteAsync(1);

        Assert.That(DecodedStrings(m_Messages.Single()), Does.Contain("階層は 9 から 12 の間で指定してください"));
        Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        m_Auth.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
    }

    private static IEnumerable<TestCaseData> EmptyCharacterValidationCases()
    {
        foreach (var game in new[] { "genshin", "hsr", "hi3" })
        {
            yield return new TestCaseData(game, "en-US", "Error when validating input:\ncharacter cannot be empty");
            yield return new TestCaseData(game, "ja", "入力の検証に失敗しました：\nキャラクターを入力してください");
            yield return new TestCaseData(game, "zh-CN", "输入验证失败：\n角色不能为空");
        }
    }

    [TestCaseSource(nameof(EmptyCharacterValidationCases))]
    public async Task CharacterModules_EmptyCharacterReturnsActionableLocalisedPrivateValidation(
        string game, string locale, string expected)
    {
        UseLocale(locale);
        using var services = new ServiceCollection()
            .AddSingleton(m_Scope.ServiceProvider.GetRequiredService<UserDbContext>())
            .AddSingleton(m_Auth.Object)
            .AddSingleton(m_Metrics.Object)
            .AddSingleton(m_Application.Object)
            .AddSingleton(m_Storage.Object)
            .AddSingleton(m_Images.Object)
            .AddSingleton<ILogger<CommandExecutorService>>(NullLogger<CommandExecutorService>.Instance)
            .BuildServiceProvider();
        var builder = new CommandExecutorBuilder(services);
        ApplicationCommandModule<ApplicationCommandContext> module = game switch
        {
            "genshin" => new GenshinCommandModule(builder, NullLogger<GenshinCommandModule>.Instance),
            "hsr" => new HsrCommandModule(builder, NullLogger<HsrCommandModule>.Instance),
            _ => new Hi3CommandModule(builder, NullLogger<Hi3CommandModule>.Instance)
        };
        var context = new ApplicationCommandContext((SlashCommandInteraction)m_Executor.Context.Interaction, m_Discord);
        // NetCord normally injects this context through an internal interface.
        typeof(ApplicationCommandModule<ApplicationCommandContext>).BaseType!
            .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, context);

        switch (module)
        {
            case GenshinCommandModule genshin:
                await genshin.CharacterCommand("");
                break;
            case HsrCommandModule hsr:
                await hsr.CharacterCommand("");
                break;
            case Hi3CommandModule hi3:
                await hi3.CharacterCommand("");
                break;
        }

        using var payload = JsonDocument.Parse(m_Messages.Single());
        Assert.Multiple(() =>
        {
            Assert.That(payload.RootElement.GetProperty("data").GetProperty("components")[0]
                .GetProperty("content").GetString(), Is.EqualTo(expected));
            Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        });
        m_Auth.Verify(x => x.GetAuthenticationAsync(It.IsAny<AuthenticationRequest>()), Times.Never);
        m_Application.Verify(x => x.ExecuteCommandAsync(It.IsAny<ExecuteRequest>(), null, null, default), Times.Never);
    }

    [Test]
    public async Task LocalisedFailureEnvelope_RemainsPrivateAndDoesNotPublishRetainedItems()
    {
        UseLocale("ja");
        Return(Result.Failure(Mehrak.Domain.Command.Models.CommandFailureReason.Timeout, CommandErrorCode.Timeout,
            outcome: RosterOutcome.Card(Game.Genshin, "uid", StorageReference.Attachment("never-publish.png"))));

        await m_Executor.ExecuteAsync(1);

        Assert.That(DecodedStrings(m_Messages.Single()), Does.Contain("コマンドの実行がタイムアウトしました")
            .And.Not.Contain("never-publish.png"));
        Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.Ephemeral));
        m_Storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), default), Times.Never);
    }

    private static string DecodedStrings(string body)
    {
        using var json = JsonDocument.Parse(body);
        return Strings(json.RootElement);

        static string Strings(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Object => string.Join("\n", value.EnumerateObject().Select(x => Strings(x.Value))),
            JsonValueKind.Array => string.Join("\n", value.EnumerateArray().Select(Strings)),
            _ => ""
        };
    }

    [Test]
    public async Task PublicCard_PrivateCompletionPrecedesPublicCardAndStreamIsDisposed()
    {
        var stream = StoreStream("actual-hash.png");
        Return(Result.Success(RosterOutcome.Card(Game.Genshin, "uid", StorageReference.Attachment("actual-hash.png"))));
        await m_Executor.ExecuteAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(m_Messages, Has.Count.EqualTo(2));
            Assert.That(m_Messages[0], Does.Contain("Command Execution Completed"));
            Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.Ephemeral));
            Assert.That(Flags(m_Messages[1]), Is.EqualTo(MessageFlags.IsComponentsV2));
            Assert.That(m_Messages[1], Does.Contain("attachment://actual-hash.png").And.Contain("remove_card").And.Contain("Remove"));
            Assert.That(stream.IsDisposed, Is.True);
        });
        m_Storage.Verify(x => x.DownloadAsync("actual-hash.png", default), Times.Once);
        m_Metrics.Verify(x => x.TrackCommand("test", 42, true), Times.Once);
    }

    [Test]
    public async Task PartialCharacters_PrivateItemMessagesPrecedeOnlyGeneratedPublicCards()
    {
        StoreStream("generated.png");
        Return(Result.Success(CharacterCardsOutcome.Create(Game.Genshin, "uid",
        [
            CharacterCardItem.Generated("a", "Alpha", 1, StorageReference.Attachment("generated.png")),
            CharacterCardItem.NotFound("missing"),
            CharacterCardItem.Failed("b", new(CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image), "Beta")
        ], OperationOutcome.Partial)));
        await m_Executor.ExecuteAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(m_Messages, Has.Count.EqualTo(2));
            Assert.That(m_Messages[0], Does.Contain("Character missing not found").And.Contain("Beta: Unable to update"));
            Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.Ephemeral));
            Assert.That(m_Messages[1], Does.Not.Contain("missing").And.Not.Contain("Beta").And.Contain("generated.png"));
            Assert.That(Flags(m_Messages[1]), Is.EqualTo(MessageFlags.IsComponentsV2));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedEnvelopeWithItems_NeverDownloadsOrPublishesCompletedItems(bool character)
    {
        CommandOutcome outcome = character
            ? CharacterCardsOutcome.Create(Game.Genshin, "uid", [CharacterCardItem.Generated("a", "Alpha", 1, StorageReference.Attachment("generated.png"))])
            : CodeRedemptionOutcome.Create(Game.Genshin, "uid", [RedemptionItem.Completed("REDEEMED", RedemptionOutcome.Redeemed)]);
        Return(Result.Failure(Mehrak.Domain.Command.Models.CommandFailureReason.Timeout, CommandErrorCode.Timeout, outcome: outcome));
        await m_Executor.ExecuteAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(m_Messages, Has.Count.EqualTo(1));
            Assert.That(m_Messages[0], Does.Contain("Command execution timed out").And.Not.Contain("generated.png").And.Not.Contain("REDEEMED"));
            Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.Ephemeral));
        });
        m_Storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), default), Times.Never);
        m_Metrics.Verify(x => x.TrackCommand("test", 42, false), Times.Once);
    }

    [Test]
    public async Task InvalidSuccessfulWireEnvelope_FailsSafelyAndPrivately()
    {
        ReturnWire(new Mehrak.Domain.Protobuf.CommandResult { IsSuccess = true });
        await m_Executor.ExecuteAsync(1);
        Assert.That(m_Messages, Has.Count.EqualTo(1));
        Assert.That(m_Messages[0], Does.Contain("unsupported or invalid response"));
        Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.Ephemeral));
        m_Metrics.Verify(x => x.TrackCommand("test", 42, false), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClientPrivateOverride_IncludingTowerHasNoRemoveButtonOrPublicCompletion(bool tower)
    {
        StoreStream("private.png");
        m_Executor.IsResponseEphemeral = true;
        Return(Result.Success(tower
            ? EndgameOutcome.Card(Game.ZenlessZoneZero, "uid", EndgameMode.SimulatedBattleTrial, StorageReference.Attachment("private.png"))
            : RosterOutcome.Card(Game.Genshin, "uid", StorageReference.Attachment("private.png"))));
        await m_Executor.ExecuteAsync(1);
        Assert.That(m_Messages, Has.Count.EqualTo(1));
        Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
        Assert.That(m_Messages[0], Does.Not.Contain("remove_card").And.Not.Contain("Command Execution Completed"));
    }

    [Test]
    public async Task Notes_UseStaticImageKeysButDiscordDisplayNamesAndDisposeAllIcons()
    {
        var icons = new[] { "resin", "expedition", "teapot", "weekly", "transformer" };
        foreach (var icon in icons)
        {
            var stream = new TrackingStream();
            m_Streams.Add(stream);
            m_Images.Setup(x => x.DownloadFileToStreamAsync($"genshin/{icon}.png", default)).ReturnsAsync(stream);
        }
        var time = DateTimeOffset.FromUnixTimeSeconds(1760000000);
        Return(Result.Success(new GenshinNotesOutcome("uid", time, new(200, 200, null), new(0, 5, ExpeditionOutcome.Undispatched, null),
            new(2400, 2400, null), 3, 3, time, new(true, time))));
        await m_Executor.ExecuteAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(m_Messages, Has.Count.EqualTo(1));
            Assert.That(Flags(m_Messages[0]), Is.EqualTo(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
            Assert.That(m_Streams.Select(x => x.IsDisposed), Is.All.True);
            foreach (var icon in icons)
            {
                Assert.That(m_Messages[0], Does.Contain($"attachment://genshin_{icon}.png"));
                Assert.That(m_Messages[0], Does.Not.Contain($"attachment://genshin/{icon}.png"));
            }
        });
        foreach (var icon in icons) m_Images.Verify(x => x.DownloadFileToStreamAsync($"genshin/{icon}.png", default), Times.Once);
        m_Storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Test]
    public async Task DiscordSendThrows_FinallyDisposesEveryDownloadedStream()
    {
        var first = StoreStream("first.png");
        var second = StoreStream("second.png");
        Return(Result.Success(CharacterCardsOutcome.Create(Game.Genshin, "uid",
        [
            CharacterCardItem.Generated("a", "A", 1, StorageReference.Attachment("first.png")),
            CharacterCardItem.Generated("b", "B", 2, StorageReference.Attachment("second.png"))
        ])));
        m_ThrowOnCardSend = true;
        await m_Executor.ExecuteAsync(1);
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(second.IsDisposed, Is.True);
        Assert.That(m_Messages.Last(), Does.Contain("An unexpected error occurred while executing"));
        Assert.That(Flags(m_Messages.Last()), Is.EqualTo(MessageFlags.Ephemeral));
        m_Metrics.Verify(x => x.TrackCommand("test", 42, false), Times.Once);
    }

    [Test]
    public async Task LaterDownloadThrows_FinallyDisposesEarlierStreamAndReturnsPrivateError()
    {
        var first = StoreStream("first.png");
        m_Storage.Setup(x => x.DownloadAsync("second.png", default)).ThrowsAsync(new IOException("Synthetic storage failure"));
        Return(Result.Success(CharacterCardsOutcome.Create(Game.Genshin, "uid",
        [
            CharacterCardItem.Generated("a", "A", 1, StorageReference.Attachment("first.png")),
            CharacterCardItem.Generated("b", "B", 2, StorageReference.Attachment("second.png"))
        ])));
        await m_Executor.ExecuteAsync(1);
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(m_Messages, Has.Count.EqualTo(1));
        Assert.That(m_Messages.Single(), Does.Contain("An unexpected error occurred").And.Not.Contain("Synthetic storage failure"));
        Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.Ephemeral));
    }

    [Test]
    public async Task MissingAttachment_IsSkippedWithoutBreakingBaselineSuccessFlow()
    {
        m_Storage.Setup(x => x.DownloadAsync("missing.png", default)).ReturnsAsync((AttachmentDownloadResult?)null);
        Return(Result.Success(RosterOutcome.Card(Game.Genshin, "uid", StorageReference.Attachment("missing.png"))));
        await m_Executor.ExecuteAsync(1);
        Assert.That(m_Messages, Has.Count.EqualTo(2));
        Assert.That(m_Messages.Last(), Does.Contain("attachment://missing.png").And.Contain("remove_card"));
        m_Metrics.Verify(x => x.TrackCommand("test", 42, true), Times.Once);
    }

    [Test]
    public async Task MissingNotesIcon_ExceptionIsSkippedAndOtherIconsDisposed()
    {
        var first = new TrackingStream();
        m_Streams.Add(first);
        m_Images.Setup(x => x.DownloadFileToStreamAsync("hsr/tbp.png", default)).ReturnsAsync(first);
        m_Images.Setup(x => x.DownloadFileToStreamAsync("hsr/weekly.png", default)).ThrowsAsync(new IOException("Synthetic image failure"));
        var time = DateTimeOffset.FromUnixTimeSeconds(1760000000);
        Return(Result.Success(new HsrNotesOutcome("uid", time, new(300, 300, null), 0, 3, time, new(0, 14000, time), new(0, 7000, time))));
        await m_Executor.ExecuteAsync(1);
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(m_Messages, Has.Count.EqualTo(1));
        Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
        m_Metrics.Verify(x => x.TrackCommand("test", 42, true), Times.Once);
    }

    [TestCase("codes")]
    [TestCase("checkin")]
    [TestCase("notfound")]
    [TestCase("limit")]
    [TestCase("empty")]
    public async Task InherentlyPrivateResults_NeverPublishEvenWithoutClientOverride(string kind)
    {
        CommandOutcome outcome = kind switch
        {
            "codes" => CodeRedemptionOutcome.Create(Game.Genshin, "uid", [RedemptionItem.Completed("CODE", RedemptionOutcome.Redeemed)]),
            "checkin" => DailyCheckInOutcome.Create([CheckInItem.Completed(Game.Genshin, CheckInOutcome.Succeeded)]),
            "notfound" => CharacterCardsOutcome.Create(Game.Genshin, "uid", [CharacterCardItem.NotFound("missing")], OperationOutcome.Empty),
            "limit" => CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a"], 4),
            _ => EndgameOutcome.Empty(Game.Genshin, "uid", EndgameMode.SpiralAbyss, 12)
        };
        Return(Result.Success(outcome));
        await m_Executor.ExecuteAsync(1);
        Assert.That(m_Messages, Has.Count.EqualTo(1));
        Assert.That(Flags(m_Messages.Single()), Is.EqualTo(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
        Assert.That(m_Messages.Single(), Does.Not.Contain("remove_card"));
    }

    [Test]
    public async Task LockedTheater_RemainsPublicWithCompletionAndRemoveButton()
    {
        Return(Result.Success(EndgameOutcome.Locked(Game.Genshin, "uid", EndgameMode.ImaginariumTheater)));
        await m_Executor.ExecuteAsync(1);
        Assert.That(m_Messages, Has.Count.EqualTo(2));
        Assert.That(m_Messages.Last(), Does.Contain("Imaginarium Theater is not unlocked").And.Contain("remove_card"));
        Assert.That(Flags(m_Messages.Last()), Is.EqualTo(MessageFlags.IsComponentsV2));
    }

    private static MessageFlags Flags(string body)
    {
        // Attachment messages are multipart: extract the payload_json part rather than file data.
        if (!body.TrimStart().StartsWith('{'))
        {
            var start = body.IndexOf('{');
            var end = body.LastIndexOf('}');
            body = body[start..(end + 1)];
        }
        using var json = JsonDocument.Parse(body);
        var payload = json.RootElement.TryGetProperty("data", out var data) ? data : json.RootElement;
        return (MessageFlags)payload.GetProperty("flags").GetInt32();
    }

    private sealed class TrackingStream() : MemoryStream(Encoding.UTF8.GetBytes("synthetic-image-bytes"))
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
