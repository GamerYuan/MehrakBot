using Mehrak.Application.Genshin.RealTimeNotes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Genshin.RealTimeNotes;

public class GenshinRealTimeNotesApplicationServiceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2025, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void BuildOutcome_PreservesCountersAndUsesOneObservationForRelativeRecoveries()
    {
        var observedAt = ObservedAt.AddMilliseconds(123).ToOffset(TimeSpan.FromHours(8));
        var outcome = GenshinRealTimeNotesApplicationService.BuildOutcome(CreateData(), Server.Asia, "uid", observedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.ObservedAt, Is.EqualTo(observedAt));
            Assert.That(outcome.ObservedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(outcome.Resin, Is.EqualTo(new NotesResource(40, 200, observedAt.AddSeconds(120))));
            Assert.That(outcome.HomeCoin, Is.EqualTo(new NotesResource(100, 2400, observedAt.AddSeconds(360))));
            Assert.That(outcome.Expeditions, Is.EqualTo(new ExpeditionNotes(2, 5, ExpeditionOutcome.InProgress, observedAt.AddSeconds(90))));
            Assert.That(outcome.RemainingWeeklyDiscounts, Is.EqualTo(2));
            Assert.That(outcome.WeeklyDiscountLimit, Is.EqualTo(3));
            Assert.That(outcome.Transformer, Is.Null);
        }
    }

    [TestCase(0, ExpeditionOutcome.Undispatched)]
    [TestCase(1, ExpeditionOutcome.Completed)]
    [TestCase(2, ExpeditionOutcome.InProgress)]
    public void BuildOutcome_ExpeditionStatesPreserveCounts(int count, ExpeditionOutcome expected)
    {
        var data = new GenshinRealTimeNotesData
        {
            CurrentExpeditionNum = count, MaxExpeditionNum = 5,
            Expeditions = count == 0 ? null : [new Expedition { RemainedTime = count == 1 ? "0" : "60" }]
        };
        var outcome = GenshinRealTimeNotesApplicationService.BuildOutcome(data, Server.Asia, "uid", ObservedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Expeditions.Current, Is.EqualTo(count));
            Assert.That(outcome.Expeditions.Maximum, Is.EqualTo(5));
            Assert.That(outcome.Expeditions.State, Is.EqualTo(expected));
            Assert.That(outcome.Expeditions.CompletionAt, Is.EqualTo(count == 2 ? ObservedAt.AddSeconds(60) : (DateTimeOffset?)null));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BuildOutcome_FullResourcesAndTransformerReached(bool reached)
    {
        var data = new GenshinRealTimeNotesData
        {
            CurrentResin = 200, MaxResin = 200, CurrentHomeCoin = 2400, MaxHomeCoin = 2400,
            Transformer = new Transformer { Obtained = true, RecoveryTime = new RecoveryTime { Reached = reached } }
        };
        var outcome = GenshinRealTimeNotesApplicationService.BuildOutcome(data, Server.Asia, "uid", ObservedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Resin, Is.EqualTo(new NotesResource(200, 200, null)));
            Assert.That(outcome.HomeCoin, Is.EqualTo(new NotesResource(2400, 2400, null)));
            Assert.That(outcome.Transformer, Is.EqualTo(new TransformerNotes(reached, outcome.WeeklyResetAt)));
        }
    }

    [Test]
    public void BuildOutcome_TransformerNotObtainedIsAbsent()
    {
        var data = new GenshinRealTimeNotesData { Transformer = new Transformer { Obtained = false } };
        Assert.That(GenshinRealTimeNotesApplicationService.BuildOutcome(data, Server.Asia, "uid", ObservedAt).Transformer, Is.Null);
    }

    [TestCase(Server.Asia, 8)]
    [TestCase(Server.Sar, 8)]
    [TestCase(Server.Europe, 1)]
    [TestCase(Server.America, -5)]
    public void BuildOutcome_WeeklyResetUsesServerLocalMondayFourAm(Server server, int offset)
    {
        var monday = new DateTimeOffset(2025, 11, 17, 4, 0, 0, TimeSpan.FromHours(offset));
        var before = GenshinRealTimeNotesApplicationService.BuildOutcome(new GenshinRealTimeNotesData(), server, "uid", monday.AddTicks(-1));
        var at = GenshinRealTimeNotesApplicationService.BuildOutcome(new GenshinRealTimeNotesData(), server, "uid", monday);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.WeeklyResetAt, Is.EqualTo(monday.ToUniversalTime()));
            Assert.That(at.WeeklyResetAt, Is.EqualTo(monday.AddDays(7).ToUniversalTime()));
            Assert.That(at.WeeklyResetAt.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsTypedNotesWithAnObservationWithinExecution()
    {
        using var factory = new TestDbContextFactory();
        var data = CreateData();
        var service = CreateService(factory, Result<GenshinRealTimeNotesData>.Success(data));
        var before = DateTimeOffset.UtcNow;
        var result = await service.ExecuteAsync(CreateContext());
        var after = DateTimeOffset.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Outcome, Is.TypeOf<GenshinNotesOutcome>());
        var outcome = (GenshinNotesOutcome)result.Outcome!;
        Assert.That(result.ToProto().ToDomain().Outcome, Is.EqualTo(outcome));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.ObservedAt, Is.InRange(before, after));
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.Resin.RecoveryAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(120)));
            Assert.That(outcome.Expeditions.CompletionAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(90)));
            Assert.That(outcome.HomeCoin.RecoveryAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(360)));
        }
    }

    [TestCase(StatusCode.ExternalServerError, CommandFailureReason.ApiError, CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes)]
    [TestCase(StatusCode.Timeout, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    [TestCase(StatusCode.Cancelled, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    public async Task ExecuteAsync_UpstreamFailuresReturnOnlySafeCodedErrors(StatusCode status, CommandFailureReason reason, CommandErrorCode code, CommandErrorTarget target)
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<GenshinRealTimeNotesData>.Failure(status, "unsafe upstream text"));
        var result = await service.ExecuteAsync(CreateContext());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(reason));
            Assert.That(result.Error, Is.EqualTo(new CommandError(code, target)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe upstream text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_MalformedDataDoesNotExposeExceptionText()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<GenshinRealTimeNotesData>.Success(new GenshinRealTimeNotesData
        {
            CurrentResin = 1, MaxResin = 200, ResinRecoveryTime = "unsafe invalid integer"
        }));
        var result = await service.ExecuteAsync(CreateContext());
        Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.Unknown, CommandErrorTarget.None)));
        Assert.That(result.Error!.Message, Does.Not.Contain("unsafe invalid integer"));
    }

    private static GenshinRealTimeNotesData CreateData() => new()
    {
        CurrentResin = 40, MaxResin = 200, ResinRecoveryTime = "120",
        CurrentHomeCoin = 100, MaxHomeCoin = 2400, HomeCoinRecoveryTime = "360",
        CurrentExpeditionNum = 2, MaxExpeditionNum = 5,
        Expeditions = [new Expedition { RemainedTime = "30" }, new Expedition { RemainedTime = "90" }],
        RemainResinDiscountNum = 2, ResinDiscountNumLimit = 3
    };

    [Test]
    public async Task ExecuteAsync_InvalidProfileReturnsOnlySafeAuthenticationError()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError),
            Result<GameProfileDto>.Failure(StatusCode.Unauthorized, "unsafe credentials text"));
        var result = await service.ExecuteAsync(CreateContext());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.AuthenticationRequired, CommandErrorTarget.GameProfile)));
            Assert.That(result.Error!.Message, Does.Not.Contain("unsafe credentials text"));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_CallerCancellationRemainsCancelledNotTimeout()
    {
        using var factory = new TestDbContextFactory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(factory, Result<GenshinRealTimeNotesData>.Failure(StatusCode.Cancelled, "unsafe upstream text"));
        var result = await service.ExecuteAsync(CreateContext(), cancellation.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.Cancelled));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.Cancelled, CommandErrorTarget.None)));
            Assert.That(result.Outcome, Is.Null);
        }
    }
    private static GenshinRealTimeNotesApplicationService CreateService(TestDbContextFactory factory, Result<GenshinRealTimeNotesData> response, Result<GameProfileDto>? profileResponse = null)
    {
        var api = new Mock<IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext>>();
        api.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var profile = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        profile.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileResponse ?? Result<GameProfileDto>.Success(new GameProfileDto { GameUid = "uid", Nickname = "player", Level = 60 }));
        return new GenshinRealTimeNotesApplicationService(api.Object, profile.Object,
            factory.CreateDbContext<UserDbContext>(), Mock.Of<ILogger<GenshinRealTimeNotesApplicationService>>());
    }

    private static IApplicationContext CreateContext()
    {
        var context = new Mock<IApplicationContext>();
        context.Setup(x => x.GetParameter("server")).Returns(Server.Asia.ToString());
        return context.Object;
    }
}
