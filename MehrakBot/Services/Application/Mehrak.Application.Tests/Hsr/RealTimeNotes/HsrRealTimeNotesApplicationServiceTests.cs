using Mehrak.Application.Hsr.RealTimeNotes;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Hsr.RealTimeNotes;

public class HsrRealTimeNotesApplicationServiceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void BuildOutcome_StaminaIsAbsoluteAndRemainingBossCountsAreNotClaimedCounts(bool full)
    {
        var observedAt = new DateTimeOffset(2025, 11, 16, 12, 0, 0, TimeSpan.FromHours(8)).AddMilliseconds(123);
        var data = new HsrRealTimeNotesData
        {
            CurrentStamina = full ? 300 : 100, MaxStamina = 300, StaminaFullTs = 1763300000,
            StaminaRecoverTime = 999, CurrentTs = 10,
            WeeklyCocoonCnt = 2, WeeklyCocoonLimit = 3,
            CurrentRogueScore = 1000, MaxRogueScore = 14000, GridFightWeeklyCur = 2000, GridFightWeeklyMax = 16000
        };
        var outcome = HsrRealTimeNotesApplicationService.BuildOutcome(data, Server.Asia, "uid", observedAt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.ObservedAt, Is.EqualTo(observedAt));
            Assert.That(outcome.ObservedAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(outcome.TrailblazePower, Is.EqualTo(new NotesResource(data.CurrentStamina, 300,
                full ? null : DateTimeOffset.FromUnixTimeSeconds(1763300000))));
            Assert.That(outcome.EchoesOfWarRemaining, Is.EqualTo(2));
            Assert.That(outcome.EchoesOfWarLimit, Is.EqualTo(3));
            Assert.That(outcome.SimulatedUniverse.Current, Is.EqualTo(1000));
            Assert.That(outcome.SimulatedUniverse.Maximum, Is.EqualTo(14000));
            Assert.That(outcome.CurrencyWars.Current, Is.EqualTo(2000));
            Assert.That(outcome.CurrencyWars.Maximum, Is.EqualTo(16000));
        }
    }

    [TestCase(Server.Asia, 8)]
    [TestCase(Server.Sar, 8)]
    [TestCase(Server.Europe, 1)]
    [TestCase(Server.America, -5)]
    public void BuildOutcome_BiweeklyResetsUseServerLocalAnchorAndExactWeeklyBoundary(Server server, int offset)
    {
        var anchor = new DateTimeOffset(2025, 11, 10, 4, 0, 0, TimeSpan.FromHours(offset));
        var atAnchor = HsrRealTimeNotesApplicationService.BuildOutcome(new HsrRealTimeNotesData(), server, "uid", anchor);
        var beforeOddWeek = HsrRealTimeNotesApplicationService.BuildOutcome(new HsrRealTimeNotesData(), server, "uid", anchor.AddDays(7).AddTicks(-1));
        var oddWeek = HsrRealTimeNotesApplicationService.BuildOutcome(new HsrRealTimeNotesData(), server, "uid", anchor.AddDays(7));
        var evenWeek = HsrRealTimeNotesApplicationService.BuildOutcome(new HsrRealTimeNotesData(), server, "uid", anchor.AddDays(14));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(atAnchor.EchoesOfWarResetAt, Is.EqualTo(anchor.AddDays(7).ToUniversalTime()));
            Assert.That(atAnchor.SimulatedUniverse.ResetAt, Is.EqualTo(anchor.AddDays(7).ToUniversalTime()));
            Assert.That(atAnchor.CurrencyWars.ResetAt, Is.EqualTo(anchor.AddDays(14).ToUniversalTime()));
            Assert.That(beforeOddWeek.SimulatedUniverse.ResetAt, Is.EqualTo(anchor.AddDays(7).ToUniversalTime()));
            Assert.That(beforeOddWeek.CurrencyWars.ResetAt, Is.EqualTo(anchor.AddDays(14).ToUniversalTime()));
            Assert.That(oddWeek.EchoesOfWarResetAt, Is.EqualTo(anchor.AddDays(14).ToUniversalTime()));
            Assert.That(oddWeek.SimulatedUniverse.ResetAt, Is.EqualTo(anchor.AddDays(21).ToUniversalTime()));
            Assert.That(oddWeek.CurrencyWars.ResetAt, Is.EqualTo(anchor.AddDays(14).ToUniversalTime()));
            Assert.That(evenWeek.SimulatedUniverse.ResetAt, Is.EqualTo(anchor.AddDays(21).ToUniversalTime()));
            Assert.That(evenWeek.CurrencyWars.ResetAt, Is.EqualTo(anchor.AddDays(28).ToUniversalTime()));
            Assert.That(oddWeek.EchoesOfWarResetAt.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void BuildOutcome_BeforeAnchorPreservesOriginalTruncationAndNegativeRemainderSemantics()
    {
        var anchor = new DateTimeOffset(2025, 11, 10, 4, 0, 0, TimeSpan.FromHours(8));
        var outcome = HsrRealTimeNotesApplicationService.BuildOutcome(new HsrRealTimeNotesData(), Server.Asia, "uid", anchor.AddDays(-8));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.SimulatedUniverse.ResetAt, Is.EqualTo(outcome.EchoesOfWarResetAt));
            Assert.That(outcome.CurrencyWars.ResetAt, Is.EqualTo(outcome.EchoesOfWarResetAt.AddDays(7)));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReturnsTypedFactsAndAnObservationWithinExecution()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<HsrRealTimeNotesData>.Success(new HsrRealTimeNotesData
        {
            CurrentStamina = 100, MaxStamina = 300, StaminaFullTs = 1763300000, WeeklyCocoonCnt = 0, WeeklyCocoonLimit = 3
        }));
        var before = DateTimeOffset.UtcNow;
        var result = await service.ExecuteAsync(CreateContext());
        var after = DateTimeOffset.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Outcome, Is.TypeOf<HsrNotesOutcome>());
        var outcome = (HsrNotesOutcome)result.Outcome!;
        Assert.That(result.ToProto().ToDomain().Outcome, Is.EqualTo(outcome));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.GameUid, Is.EqualTo("uid"));
            Assert.That(outcome.ObservedAt, Is.InRange(before, after));
            Assert.That(outcome.TrailblazePower.RecoveryAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(1763300000)));
            Assert.That(outcome.EchoesOfWarRemaining, Is.Zero);
        }
    }

    [TestCase(StatusCode.ExternalServerError, CommandFailureReason.ApiError, CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes)]
    [TestCase(StatusCode.Timeout, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    [TestCase(StatusCode.Cancelled, CommandFailureReason.Timeout, CommandErrorCode.Timeout, CommandErrorTarget.None)]
    public async Task ExecuteAsync_UpstreamFailuresReturnOnlySafeCodedErrors(StatusCode status, CommandFailureReason reason, CommandErrorCode code, CommandErrorTarget target)
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<HsrRealTimeNotesData>.Failure(status, "unsafe upstream text"));
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
    public async Task ExecuteAsync_InvalidProfileReturnsOnlySafeAuthenticationError()
    {
        using var factory = new TestDbContextFactory();
        var service = CreateService(factory, Result<HsrRealTimeNotesData>.Failure(StatusCode.ExternalServerError),
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
        var service = CreateService(factory, Result<HsrRealTimeNotesData>.Failure(StatusCode.Cancelled, "unsafe upstream text"));
        var result = await service.ExecuteAsync(CreateContext(), cancellation.Token);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.Cancelled));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.Cancelled, CommandErrorTarget.None)));
            Assert.That(result.Outcome, Is.Null);
        }
    }
    private static HsrRealTimeNotesApplicationService CreateService(TestDbContextFactory factory, Result<HsrRealTimeNotesData> response, Result<GameProfileDto>? profileResponse = null)
    {
        var api = new Mock<IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>>();
        api.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var profile = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        profile.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileResponse ?? Result<GameProfileDto>.Success(new GameProfileDto { GameUid = "uid", Nickname = "player", Level = 60 }));
        return new HsrRealTimeNotesApplicationService(api.Object, profile.Object,
            factory.CreateDbContext<UserDbContext>(), Mock.Of<ILogger<HsrRealTimeNotesApplicationService>>());
    }

    private static IApplicationContext CreateContext()
    {
        var context = new Mock<IApplicationContext>();
        context.Setup(x => x.GetParameter("server")).Returns(Server.Asia.ToString());
        return context.Object;
    }
}
