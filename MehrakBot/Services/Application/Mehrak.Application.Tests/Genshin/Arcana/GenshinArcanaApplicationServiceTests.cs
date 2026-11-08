#region

using Mehrak.Application.Genshin.Arcana;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Card;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Abstractions;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared.Types;
using Mehrak.Infrastructure.User;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Genshin.Arcana;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinArcanaApplicationServiceTests
{
    private TestDbContextFactory m_DbFactory = null!;

    [SetUp]
    public void Setup()
    {
        m_DbFactory = new TestDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        m_DbFactory.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_Unauthorized_ReturnsAuthErrorInsteadOfLocked()
    {
        var (service, apiMock, gameRoleApiMock, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GameApiResponse<GenshinTheaterResponseData>>.Failure(
                StatusCode.Unauthorized, "Invalid HoYoLAB UID or Cookies"));

        var result = await service.ExecuteAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.AuthError));
            Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.AuthenticationRequired,
                CommandErrorTarget.Arcana)));
            Assert.That(result.Outcome, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_Locked_ReturnsNotUnlockedOutcome()
    {
        var (service, apiMock, gameRoleApiMock, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                GameApiResponse<GenshinTheaterResponseData>.Locked()));

        var result = await service.ExecuteAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var outcome = (EndgameOutcome)result.Outcome!;
            Assert.That(outcome.State, Is.EqualTo(OperationOutcome.NotUnlocked));
            Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.Arcana));
            Assert.That(outcome.Game, Is.EqualTo(Game.Genshin));
        }
    }

    [Test]
    public async Task ExecuteAsync_ApiEmptyOutcome_ReturnsNoClearRecords()
    {
        var (service, apiMock, gameRoleApiMock, _, _) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                GameApiResponse<GenshinTheaterResponseData>.Empty()));

        var result = await service.ExecuteAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var outcome = (EndgameOutcome)result.Outcome!;
            Assert.That(outcome.State, Is.EqualTo(OperationOutcome.Empty));
            Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.Arcana));
        }
    }

    [Test]
    public async Task ExecuteAsync_AvailableWithTarotState_ReturnsCardOutcome()
    {
        var (service, apiMock, gameRoleApiMock, cardServiceMock, attachmentStorageMock) = SetupMocks();
        gameRoleApiMock.Setup(x => x.GetAsync(It.IsAny<GameRoleApiContext>()))
            .ReturnsAsync(Result<GameProfileDto>.Success(CreateTestProfile()));
        apiMock.Setup(x => x.GetAsync(It.IsAny<BaseHoYoApiContext>()))
            .ReturnsAsync(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                GameApiResponse<GenshinTheaterResponseData>.Available(new GenshinTheaterResponseData
                {
                    Data = [],
                    IsUnlock = true,
                    TarotCardState = new GenshinTarotCardState
                    {
                        TotalNum = 22,
                        CurrNum = 1,
                        List =
                        [
                            new GenshinTarotCard
                            {
                                Name = "The Magician",
                                IsUnlock = true,
                                UnlockNum = 1
                            }
                        ]
                    }
                })));
        attachmentStorageMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        attachmentStorageMock.Setup(x => x.StoreAsync(It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        cardServiceMock.Setup(x => x.GetCardAsync(It.IsAny<ICardGenerationContext<GenshinTarotCardState>>()))
            .ReturnsAsync(new MemoryStream());

        var result = await service.ExecuteAsync(CreateContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            var outcome = (EndgameOutcome)result.Outcome!;
            Assert.That(outcome.State, Is.EqualTo(OperationOutcome.Complete));
            Assert.That(outcome.Mode, Is.EqualTo(EndgameMode.Arcana));
            Assert.That(outcome.Image, Is.Not.Null);
        }
    }

    private (GenshinArcanaApplicationService Service,
        Mock<IApiService<GameApiResponse<GenshinTheaterResponseData>, BaseHoYoApiContext>> ApiMock,
        Mock<IApiService<GameProfileDto, GameRoleApiContext>> GameRoleApiMock,
        Mock<ICardService<GenshinTarotCardState>> CardServiceMock,
        Mock<IAttachmentStorageService> AttachmentStorageMock) SetupMocks()
    {
        var cardServiceMock = new Mock<ICardService<GenshinTarotCardState>>();
        var apiMock = new Mock<IApiService<GameApiResponse<GenshinTheaterResponseData>, BaseHoYoApiContext>>();
        var gameRoleApiMock = new Mock<IApiService<GameProfileDto, GameRoleApiContext>>();
        var attachmentStorageMock = new Mock<IAttachmentStorageService>();
        var userContext = m_DbFactory.CreateDbContext<UserDbContext>();

        var service = new GenshinArcanaApplicationService(
            cardServiceMock.Object,
            apiMock.Object,
            gameRoleApiMock.Object,
            userContext,
            attachmentStorageMock.Object,
            Mock.Of<ILogger<GenshinArcanaApplicationService>>());

        return (service, apiMock, gameRoleApiMock, cardServiceMock, attachmentStorageMock);
    }

    private static GameProfileDto CreateTestProfile()
    {
        return new GameProfileDto
        {
            GameUid = "800000000",
            Nickname = "TestPlayer",
            Level = 60
        };
    }

    private static IApplicationContext CreateContext()
    {
        var mock = new Mock<IApplicationContext>();
        mock.Setup(x => x.UserId).Returns(1);
        mock.SetupGet(x => x.LtUid).Returns(1ul);
        mock.SetupGet(x => x.LToken).Returns("test");
        mock.Setup(x => x.GetParameter(It.IsAny<string>()))
            .Returns((string key) => key == "server" ? Server.Asia.ToString() : null);
        return mock.Object;
    }
}
