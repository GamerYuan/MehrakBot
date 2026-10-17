using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Application.Zzz.RealTimeNotes;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Zzz.RealTimeNotes;

public class ZzzRealTimeNotesApplicationServiceTests
{
    private static readonly DateTimeOffset ObservedAt = new DateTimeOffset(2025, 11, 16, 12, 0, 0, TimeSpan.FromHours(8)).AddMilliseconds(123);

    [Test]
    public void BuildOutcome_PreservesRelativeAndAbsoluteTimesAndAllCounts()
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(), "uid", ObservedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.ObservedAt, Is.EqualTo(ObservedAt));
            Assert.That(outcome.ObservedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(outcome.Battery, Is.EqualTo(new NotesResource(40, 240, ObservedAt.AddSeconds(120))));
            Assert.That(outcome.DailyEngagement, Is.EqualTo(new NotesCounter(100, 400, null)));
            Assert.That(outcome.BountyCommission, Is.EqualTo(new NotesCounter(0, 3, ObservedAt.AddSeconds(360))));
            Assert.That(outcome.WeeklyPoints, Is.EqualTo(new NotesCounter(100, 800, ObservedAt.AddSeconds(600))));
            Assert.That(outcome.Omnicoins, Is.EqualTo(new NotesCounter(0, 1000, DateTimeOffset.FromUnixTimeSeconds(1763300000))));
        }
    }

    [Test]
    public void BuildOutcome_UnavailableFactsAreAbsentAndRestoreZeroMeansRecoveredEvenBelowMaximum()
    {
        var data = CreateData(unavailable: true, restore: 0);
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(data, "uid", ObservedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Battery, Is.EqualTo(new NotesResource(40, 240, null)));
            Assert.That(outcome.BountyCommission, Is.Null);
            Assert.That(outcome.WeeklyPoints, Is.Null);
            Assert.That(outcome.Omnicoins, Is.Null);
        }
    }

    [TestCase(CardSignState.CardSignNo, ScratchCardOutcome.Incomplete)]
    [TestCase(CardSignState.CardSignDone, ScratchCardOutcome.Completed)]
    [TestCase((CardSignState)99, ScratchCardOutcome.Unknown)]
    public void BuildOutcome_ScratchCardUsesExplicitSemanticMapping(CardSignState source, ScratchCardOutcome expected)
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(card: source), "uid", ObservedAt);
        Assert.That(outcome.ScratchCard, Is.EqualTo(expected));
    }

    [TestCase(VhsSaleState.SaleStateNo, VideoStoreOutcome.WaitingToOpen)]
    [TestCase(VhsSaleState.SaleStateDoing, VideoStoreOutcome.Open)]
    [TestCase(VhsSaleState.SaleStateDone, VideoStoreOutcome.RevenueAvailable)]
    [TestCase((VhsSaleState)99, VideoStoreOutcome.Unknown)]
    public void BuildOutcome_VideoStoreUsesExplicitSemanticMapping(VhsSaleState source, VideoStoreOutcome expected)
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(video: source), "uid", ObservedAt);
        Assert.That(outcome.VideoStore, Is.EqualTo(expected));
    }

    [TestCase(ExpeditionState.ExpeditionStateInCanSend, TempleAdventureOutcome.Dispatchable)]
    [TestCase(ExpeditionState.ExpeditionStateInProgress, TempleAdventureOutcome.Adventuring)]
    [TestCase(ExpeditionState.ExpeditionStateEnd, TempleAdventureOutcome.Completed)]
    [TestCase((ExpeditionState)99, TempleAdventureOutcome.Unknown)]
    public void BuildOutcome_AdventureUsesExplicitSemanticMapping(ExpeditionState source, TempleAdventureOutcome expected)
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(adventure: source), "uid", ObservedAt);
        Assert.That(outcome.Adventure, Is.EqualTo(expected));
    }

    [TestCase(BenchState.BenchStateProducing, TempleCraftingOutcome.Crafting)]
    [TestCase(BenchState.BenchStateCanProduce, TempleCraftingOutcome.Craftable)]
    [TestCase((BenchState)99, TempleCraftingOutcome.Unknown)]
    public void BuildOutcome_CraftingUsesExplicitSemanticMapping(BenchState source, TempleCraftingOutcome expected)
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(crafting: source), "uid", ObservedAt);
        Assert.That(outcome.Crafting, Is.EqualTo(expected));
    }

    [TestCase(ShelveState.ShelveStateCanSell, TempleSalesOutcome.NotSelling)]
    [TestCase(ShelveState.ShelveStateSelling, TempleSalesOutcome.Selling)]
    [TestCase(ShelveState.ShelveStateSoldOut, TempleSalesOutcome.OutOfStock)]
    [TestCase((ShelveState)99, TempleSalesOutcome.Unknown)]
    public void BuildOutcome_SalesUsesExplicitSemanticMapping(ShelveState source, TempleSalesOutcome expected)
    {
        var outcome = ZzzRealTimeNotesApplicationService.BuildOutcome(CreateData(sales: source), "uid", ObservedAt);
        Assert.That(outcome.Sales, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteAsync_ReturnsTypedNotesUsingOneObservation()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<ZzzRealTimeNotesData>.Success(CreateData()));
        var before = DateTimeOffset.UtcNow;
        var result = await service.ExecuteAsync(CreateContext());
        var after = DateTimeOffset.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Outcome, Is.TypeOf<ZzzNotesOutcome>());
        var outcome = (ZzzNotesOutcome)result.Outcome!;
        Assert.That(result.ToProto().ToDomain().Outcome, Is.EqualTo(outcome));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.ObservedAt, Is.InRange(before, after));
            Assert.That(outcome.Battery.RecoveryAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(120)));
            Assert.That(outcome.BountyCommission!.ResetAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(360)));
            Assert.That(outcome.WeeklyPoints!.ResetAt, Is.EqualTo(outcome.ObservedAt.AddSeconds(600)));
            Assert.That(outcome.Omnicoins!.ResetAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1763300000)));
        }
    }

    [TestCase(StatusCode.ExternalServerError, CommandFailureReason.ApiError, CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes)]
    [TestCase(StatusCode.Timeout, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    [TestCase(StatusCode.Cancelled, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    public async Task ExecuteAsync_UpstreamFailuresReturnOnlySafeCodedErrors(StatusCode status, CommandFailureReason reason, CommandErrorCode code, CommandErrorTarget target)
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<ZzzRealTimeNotesData>.Failure(status, "unsafe upstream text"));
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

    private static ZzzRealTimeNotesData CreateData(bool unavailable = false, int restore = 120,
        CardSignState card = CardSignState.CardSignNo, VhsSaleState video = VhsSaleState.SaleStateNo,
        ExpeditionState adventure = ExpeditionState.ExpeditionStateInProgress,
        BenchState crafting = BenchState.BenchStateProducing, ShelveState sales = ShelveState.ShelveStateSelling) => new()
        {
            Energy = new EnergyInfo { Progress = new RegenProgress { Current = 40, Max = 240 }, Restore = restore },
            Vitality = new RegenProgress { Current = 100, Max = 400 },
            CardSign = card, VhsSale = new VhsSale { SaleState = video },
            BountyCommission = new BountyCommissionInfo { Num = 0, Total = unavailable ? 0 : 3, RefreshTime = 360 },
            WeeklyTask = unavailable ? null : new WeeklyTaskInfo { CurPoint = 100, MaxPoint = 800, RefreshTime = 600 },
            TempleManage = new TempleManageInfo
            {
                CurrentCurrency = 0, WeeklyCurrencyMax = 1000, CurrencyNextRefreshTs = unavailable ? 0 : 1763300000,
                ExpeditionState = adventure, BenchState = crafting, ShelveState = sales
            }
        };

    [Test]
    public async Task ExecuteAsync_InvalidProfileReturnsOnlySafeAuthenticationError()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<ZzzRealTimeNotesData>.Failure(StatusCode.ExternalServerError),
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
        var service = CreateService(factory, Result<ZzzRealTimeNotesData>.Failure(StatusCode.Cancelled, "unsafe upstream text"));
        var result = await service.ExecuteAsync(CreateContext(), cancellation.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.Cancelled));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.Cancelled, CommandErrorTarget.None)));
            Assert.That(result.Outcome, Is.Null);
        }
    }
    private static ZzzRealTimeNotesApplicationService CreateService(TestDbContextFactory factory, Result<ZzzRealTimeNotesData> response, Result<GameProfileDto>? profileResponse = null)
    {
        var api = new Mock<IApiService<ZzzRealTimeNotesData, BaseHoYoApiContext>>();
        api.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var profile = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        profile.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileResponse ?? Result<GameProfileDto>.Success(new GameProfileDto { GameUid = "uid", Nickname = "player", Level = 60 }));
        return new ZzzRealTimeNotesApplicationService(api.Object, profile.Object,
            factory.CreateDbContext<UserDbContext>(), Mock.Of<ILogger<ZzzRealTimeNotesApplicationService>>());
    }

    private static IApplicationContext CreateContext()
    {
        var context = new Mock<IApplicationContext>();
        context.Setup(x => x.GetParameter("server")).Returns(Server.Asia.ToString());
        return context.Object;
    }
}
