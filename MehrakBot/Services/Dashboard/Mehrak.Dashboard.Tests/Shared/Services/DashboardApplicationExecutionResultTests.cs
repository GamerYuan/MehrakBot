using System.Text.Json;
using Google.Protobuf;
using Mehrak.Dashboard.Shared.Models;
using Mehrak.Dashboard.Shared.Services;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Mvc;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Dashboard.Tests.Shared.Services;

[TestFixture]
public class DashboardApplicationExecutionResultTests
{
    private const string FirstKey = "first.png";
    private const string SecondKey = "second.png";
    private const string Uid = "123456789";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ObjectResult Map(CommandResult result) =>
        (ObjectResult)DashboardApplicationExecutionResult.FromCommandResult(
            Proto.CommandResult.Parser.ParseFrom(result.ToProto().ToByteArray()).ToDomain()).MapToActionResult();

    private static JsonElement Json(ObjectResult result) =>
        JsonSerializer.SerializeToElement(result.Value, result.Value!.GetType(), JsonOptions);

    [TestCase(Game.Genshin)]
    [TestCase(Game.HonkaiStarRail)]
    [TestCase(Game.ZenlessZoneZero)]
    [TestCase(Game.HonkaiImpact3)]
    public void Characters_ExposeItemsAndFirstGeneratedCardInOriginalOrder(Game game)
    {
        var outcome = CharacterCardsOutcome.Create(game, Uid,
        [
            CharacterCardItem.NotFound("missing"),
            CharacterCardItem.Generated("alias", "First", 1, StorageReference.Attachment(FirstKey)),
            CharacterCardItem.Failed("failed", new(CommandErrorCode.ApiUnavailable, CommandErrorTarget.CharacterData), "Failed", 2),
            CharacterCardItem.Generated("second", "Second", 3, StorageReference.Attachment(SecondKey)),
            CharacterCardItem.NotAttempted("later")
        ], OperationOutcome.Partial);
        var result = Map(CommandResult.Success(outcome));
        var dto = (DashboardCharacterCardsDto)result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(200));
            Assert.That(dto.StorageFileName, Is.EqualTo(FirstKey));
            Assert.That(dto.Items.Select(item => item.RequestedName), Is.EqualTo(new[] { "missing", "alias", "failed", "second", "later" }));
            Assert.That(dto.Items[0].Error!.Code, Is.EqualTo(CommandErrorCode.CharacterNotFound));
            Assert.That(dto.Items[1].CharacterName, Is.EqualTo("First"));
            Assert.That(dto.Items[1].CharacterId, Is.EqualTo(1));
            Assert.That(dto.Items[2].Error!.Error, Is.EqualTo(new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.None).Message));
            Assert.That(dto.Items[3].StorageFileName, Is.EqualTo(SecondKey));
        });
        var json = Json(result);
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("storageFileName").GetString(), Is.EqualTo(FirstKey));
            Assert.That(json.GetProperty("game").GetString(), Is.EqualTo(game.ToString()));
            Assert.That(json.GetProperty("gameUid").GetString(), Is.EqualTo(Uid));
            Assert.That(json.GetProperty("state").GetString(), Is.EqualTo("Partial"));
            Assert.That(json.GetProperty("code").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetProperty("items")[0].GetProperty("status").GetString(), Is.EqualTo("NotFound"));
            Assert.That(json.GetProperty("items")[0].GetProperty("error").GetProperty("code").GetString(), Is.EqualTo("CharacterNotFound"));
        });
    }

    [TestCase(Game.Genshin)]
    [TestCase(Game.HonkaiStarRail)]
    [TestCase(Game.ZenlessZoneZero)]
    [TestCase(Game.HonkaiImpact3)]
    public void Characters_CompleteAndNotFoundAndLimitAreTypedSuccessfulResponses(Game game)
    {
        var complete = Map(CommandResult.Success(CharacterCardsOutcome.Create(game, Uid,
            [CharacterCardItem.Generated("alias", "Name", 1, StorageReference.Attachment(FirstKey))])));
        var empty = Map(CommandResult.Success(CharacterCardsOutcome.Create(game, Uid,
            [CharacterCardItem.NotFound("missing")], OperationOutcome.Empty)));
        var limit = Map(CommandResult.Success(CharacterCardsOutcome.LimitExceeded(game, ["a", "b"], 1)));
        Assert.Multiple(() =>
        {
            Assert.That(complete.StatusCode, Is.EqualTo(200));
            Assert.That(((DashboardCharacterCardsDto)complete.Value!).StorageFileName, Is.EqualTo(FirstKey));
            Assert.That(empty.StatusCode, Is.EqualTo(200));
            Assert.That(((DashboardCharacterCardsDto)empty.Value!).StorageFileName, Is.Null);
            Assert.That(((DashboardCharacterCardsDto)empty.Value!).State, Is.EqualTo(OperationOutcome.Empty));
            Assert.That(limit.StatusCode, Is.EqualTo(200));
            Assert.That(((DashboardCharacterCardsDto)limit.Value!).RequestLimit, Is.EqualTo(1));
            Assert.That(((DashboardCharacterCardsDto)limit.Value!).Items.All(item => item.Status == CharacterCardStatus.NotAttempted), Is.True);
            Assert.That(Json(limit).GetProperty("state").GetString(), Is.EqualTo("RequestLimitExceeded"));
            Assert.That(Json(limit).GetProperty("code").GetString(), Is.EqualTo("RequestLimitExceeded"));
            Assert.That(Json(limit).GetProperty("storageFileName").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Json(limit).GetProperty("gameUid").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [TestCase(OperationOutcome.Empty)]
    [TestCase(OperationOutcome.Partial)]
    [TestCase(OperationOutcome.Failed)]
    public void Characters_ValidNoImageStateDoesNotInventMissingImageFailure(OperationOutcome state)
    {
        var result = Map(CommandResult.Success(CharacterCardsOutcome.Create(Game.Genshin, Uid,
            [CharacterCardItem.Failed("failed", new(CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image))], state)));
        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(((DashboardCharacterCardsDto)result.Value!).StorageFileName, Is.Null);
        Assert.That(((DashboardCharacterCardsDto)result.Value!).State, Is.EqualTo(state));
    }

    [TestCase(Game.Genshin)]
    [TestCase(Game.HonkaiStarRail)]
    [TestCase(Game.ZenlessZoneZero)]
    public void Roster_AvailableAndEmptyAreTypedSuccess(Game game)
    {
        var card = Map(CommandResult.Success(RosterOutcome.Card(game, Uid, StorageReference.Attachment(FirstKey))));
        var empty = Map(CommandResult.Success(RosterOutcome.Empty(game, Uid)));
        Assert.Multiple(() =>
        {
            Assert.That(card.StatusCode, Is.EqualTo(200));
            Assert.That(empty.StatusCode, Is.EqualTo(200));
            Assert.That(((DashboardRosterDto)card.Value!).StorageFileName, Is.EqualTo(FirstKey));
            Assert.That(((DashboardRosterDto)empty.Value!).StorageFileName, Is.Null);
            Assert.That(Json(card).GetProperty("state").GetString(), Is.EqualTo("Complete"));
            Assert.That(Json(card).GetProperty("game").GetString(), Is.EqualTo(game.ToString()));
            Assert.That(Json(empty).GetProperty("state").GetString(), Is.EqualTo("Empty"));
        });
    }

    private static IEnumerable<TestCaseData> Endgames() => new[]
    {
        (Game.Genshin, EndgameMode.SpiralAbyss),
        (Game.Genshin, EndgameMode.ImaginariumTheater),
        (Game.Genshin, EndgameMode.StygianOnslaught),
        (Game.HonkaiStarRail, EndgameMode.MemoryOfChaos),
        (Game.HonkaiStarRail, EndgameMode.PureFiction),
        (Game.HonkaiStarRail, EndgameMode.ApocalypticShadow),
        (Game.HonkaiStarRail, EndgameMode.AnomalyArbitration),
        (Game.ZenlessZoneZero, EndgameMode.ShiyuDefense),
        (Game.ZenlessZoneZero, EndgameMode.DeadlyAssault),
        (Game.ZenlessZoneZero, EndgameMode.SimulatedBattleTrial)
    }.Select(pair => new TestCaseData(pair.Item1, pair.Item2));

    [TestCaseSource(nameof(Endgames))]
    public void Endgame_CardMetadataAndEmptyStates(Game game, EndgameMode mode)
    {
        var start = new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567);
        var end = start.AddDays(30);
        var hasCycle = mode != EndgameMode.SimulatedBattleTrial;
        int? floor = mode == EndgameMode.SpiralAbyss ? 12 : null;
        var card = Map(CommandResult.Success(EndgameOutcome.Card(game, Uid, mode, StorageReference.Attachment(FirstKey),
            hasCycle ? start : null, hasCycle ? end : null, floor)));
        var empty = Map(CommandResult.Success(EndgameOutcome.Empty(game, Uid, mode, floor)));
        var dto = (DashboardEndgameDto)card.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(card.StatusCode, Is.EqualTo(200));
            Assert.That(dto.StorageFileName, Is.EqualTo(FirstKey));
            Assert.That(dto.Game, Is.EqualTo(game));
            Assert.That(dto.Mode, Is.EqualTo(mode));
            Assert.That(dto.Floor, Is.EqualTo(floor));
            Assert.That(dto.CycleStart, Is.EqualTo(hasCycle ? start.ToUniversalTime() : null));
            Assert.That(dto.CycleEnd, Is.EqualTo(hasCycle ? end.ToUniversalTime() : null));
            Assert.That(empty.StatusCode, Is.EqualTo(200));
            Assert.That(((DashboardEndgameDto)empty.Value!).State, Is.EqualTo(OperationOutcome.Empty));
            Assert.That(((DashboardEndgameDto)empty.Value!).StorageFileName, Is.Null);
            Assert.That(((DashboardEndgameDto)empty.Value!).Floor, Is.EqualTo(floor));
            Assert.That(Json(empty).GetProperty("cycleStart").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Json(empty).GetProperty("cycleEnd").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
        var json = Json(card);
        Assert.That(json.GetProperty("mode").GetString(), Is.EqualTo(mode.ToString()));
        Assert.That(json.GetProperty("game").GetString(), Is.EqualTo(game.ToString()));
        Assert.That(json.GetProperty("state").GetString(), Is.EqualTo("Complete"));
        if (hasCycle)
        {
            Assert.That(dto.CycleStart!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(json.GetProperty("cycleStart").GetDateTimeOffset().Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(json.GetProperty("cycleStart").GetDateTimeOffset(), Is.EqualTo(start.ToUniversalTime()));
        }
        else
            Assert.That(json.GetProperty("cycleStart").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [TestCase(EndgameMode.ImaginariumTheater)]
    [TestCase(EndgameMode.StygianOnslaught)]
    public void Endgame_NotUnlockedIsSuccessfulTypedEmptyState(EndgameMode mode)
    {
        var result = Map(CommandResult.Success(EndgameOutcome.Locked(Game.Genshin, Uid, mode)));
        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(Json(result).GetProperty("state").GetString(), Is.EqualTo("NotUnlocked"));
        Assert.That(Json(result).GetProperty("storageFileName").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Endgame_AbsentCycleOnAvailableCardStaysAbsent()
    {
        var result = Map(CommandResult.Success(EndgameOutcome.Card(Game.Genshin, Uid,
            EndgameMode.SpiralAbyss, StorageReference.Attachment(FirstKey))));
        Assert.That(result.StatusCode, Is.EqualTo(200));
        Assert.That(Json(result).GetProperty("cycleStart").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Json(result).GetProperty("cycleEnd").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Json(result).GetProperty("floor").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Failure_AllCodesRemainHttp500AndUseOnlySafeErrorIgnoringRetainedCards()
    {
        var outcome = CharacterCardsOutcome.Create(Game.Genshin, Uid,
            [CharacterCardItem.Generated("a", "A", 1, StorageReference.Attachment(FirstKey))], OperationOutcome.Partial);
        foreach (var code in Enum.GetValues<CommandErrorCode>())
        {
            var error = new CommandError(code, CommandErrorTarget.CharacterData);
            var result = Map(CommandResult.Failure(CommandFailureReason.ApiError, error, outcome));
            Assert.Multiple(() =>
            {
                Assert.That(result.StatusCode, Is.EqualTo(500));
                Assert.That(Json(result).GetProperty("error").GetString(), Is.EqualTo(error.Message));
                Assert.That(Json(result).GetProperty("code").GetString(), Is.EqualTo(code.ToString()));
                Assert.That(Json(result).TryGetProperty("storageFileName", out _), Is.False);
                Assert.That(Json(result).TryGetProperty("items", out _), Is.False);
            });
        }
    }

    private static IEnumerable<TestCaseData> MalformedResponses()
    {
        yield return new(new Proto.CommandResult { IsSuccess = true });
        yield return new(new Proto.CommandResult { IsSuccess = false });
        yield return new(new Proto.CommandResult { IsSuccess = true, Roster = new() { Game = Proto.CommandGame.Genshin, GameUid = Uid, State = Proto.OperationOutcome.Complete } });
        yield return new(new Proto.CommandResult { IsSuccess = true, Endgame = new() { Game = Proto.CommandGame.Genshin, GameUid = Uid, Mode = Proto.EndgameMode.SpiralAbyss, State = Proto.OperationOutcome.Complete } });
        var characters = CommandResult.Success(CharacterCardsOutcome.Create(Game.Genshin, Uid,
            [CharacterCardItem.Generated("a", "A", 1, StorageReference.Attachment(FirstKey))])).ToProto();
        characters.CharacterCards.Items[0].Image = null;
        yield return new(characters);
        var malformedSecond = CommandResult.Success(CharacterCardsOutcome.Create(Game.Genshin, Uid,
            [CharacterCardItem.Generated("a", "A", 1, StorageReference.Attachment(FirstKey)),
                CharacterCardItem.Generated("b", "B", 2, StorageReference.Attachment(SecondKey))])).ToProto();
        malformedSecond.CharacterCards.Items[1].Image.StorageFileName = "";
        yield return new(malformedSecond);
        var emptyKey = CommandResult.Success(RosterOutcome.Card(Game.Genshin, Uid, StorageReference.Attachment(FirstKey))).ToProto();
        emptyKey.Roster.Image.StorageFileName = "  ";
        yield return new(emptyKey);
        var source = CommandResult.Success(RosterOutcome.Card(Game.Genshin, Uid, StorageReference.Attachment(FirstKey))).ToProto();
        source.Roster.Image.SourceType = (Proto.AttachmentSourceType)999;
        yield return new(source);
        var wrongSource = CommandResult.Success(RosterOutcome.Card(Game.Genshin, Uid, StorageReference.Image(FirstKey))).ToProto();
        yield return new(wrongSource);
        var badTime = CommandResult.Success(EndgameOutcome.Card(Game.Genshin, Uid, EndgameMode.SpiralAbyss,
            StorageReference.Attachment(FirstKey))).ToProto();
        badTime.Endgame.CycleStart = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = long.MaxValue };
        yield return new(badTime);
        var contradictory = CommandResult.Success(EndgameOutcome.Empty(Game.Genshin, Uid, EndgameMode.SpiralAbyss)).ToProto();
        contradictory.Endgame.Image = StorageReference.Attachment(FirstKey).ToProto();
        yield return new(contradictory);
        var unknownError = new Proto.CommandResult { IsSuccess = false, Error = new() { Code = (Proto.CommandErrorCode)999 } };
        yield return new(unknownError);
    }

    [TestCaseSource(nameof(MalformedResponses))]
    public void MalformedWire_IsSafeInvalidResponse(Proto.CommandResult proto)
    {
        var result = (ObjectResult)DashboardApplicationExecutionResult.FromCommandResult(
            Proto.CommandResult.Parser.ParseFrom(proto.ToByteArray()).ToDomain()).MapToActionResult();
        AssertInvalidResponse(result);
    }

    [Test]
    public void MalformedDirectDomain_IsRejectedSafely()
    {
        var result = (ObjectResult)DashboardApplicationExecutionResult.FromCommandResult(CommandResult.Success(
            new RosterOutcome(Game.Genshin, Uid, OperationOutcome.Complete, null))).MapToActionResult();
        AssertInvalidResponse(result);
    }

    [Test]
    public void UnsupportedFamilies_AreExplicitlyRejected()
    {
        CommandOutcome[] outcomes =
        [
            CodeRedemptionOutcome.Empty(Game.Genshin, Uid), DailyCheckInOutcome.Empty(),
            new GenshinNotesOutcome(Uid, DateTimeOffset.UtcNow, new(1, 2, null), new(0, 5, ExpeditionOutcome.Undispatched, null),
                new(1, 2, null), 3, 3, DateTimeOffset.UtcNow, null),
            new HsrNotesOutcome(Uid, DateTimeOffset.UtcNow, new(1, 2, null), 3, 3, DateTimeOffset.UtcNow, new(0, 1, null), new(0, 1, null)),
            new ZzzNotesOutcome(Uid, DateTimeOffset.UtcNow, new(1, 2, null), new(1, 2, null), ScratchCardOutcome.Unknown,
                VideoStoreOutcome.Unknown, null, null, null, TempleAdventureOutcome.Unknown, TempleCraftingOutcome.Unknown, TempleSalesOutcome.Unknown)
        ];
        foreach (var outcome in outcomes) AssertInvalidResponse(Map(CommandResult.Success(outcome)));
    }

    [Test]
    public void AuthAndValidationStatusMappingsRemainUnchanged()
    {
        (DashboardApplicationExecutionResult Result, int Status, string? Code)[] cases =
        [
            (DashboardApplicationExecutionResult.ValidationFailed(["bad"]), 400, null),
            (DashboardApplicationExecutionResult.AuthenticationRequired("unlock"), 403, "AUTH_REQUIRED"),
            (DashboardApplicationExecutionResult.AuthenticationFailed("invalid"), 401, null),
            (DashboardApplicationExecutionResult.NotFound("missing"), 404, null),
            (DashboardApplicationExecutionResult.RateLimited("later"), 429, null),
            (DashboardApplicationExecutionResult.Error("failed"), 500, null)
        ];
        foreach (var item in cases)
        {
            var result = (ObjectResult)item.Result.MapToActionResult();
            Assert.That(result.StatusCode, Is.EqualTo(item.Status));
            var json = Json(result);
            if (item.Code is not null) Assert.That(json.GetProperty("code").GetString(), Is.EqualTo(item.Code));
            if (item.Status == 400) Assert.That(json.GetProperty("validationErrors")[0].GetString(), Is.EqualTo("bad"));
        }
    }

    [Test]
    public void CardRoutesAndAuthorizationRemainUnchanged()
    {
        (Type Controller, string Prefix, string[] Routes)[] controllers =
        [
            (typeof(Mehrak.Dashboard.Genshin.GenshinController), "genshin", ["character", "abyss", "theater", "stygian", "charlist"]),
            (typeof(Mehrak.Dashboard.Hsr.HsrController), "hsr", ["character", "moc", "pf", "as", "charlist", "aa"]),
            (typeof(Mehrak.Dashboard.Zzz.ZzzController), "zzz", ["character", "shiyu", "da", "tower", "charlist"]),
            (typeof(Mehrak.Dashboard.Hi3.Hi3Controller), "hi3", ["battlesuit"])
        ];
        foreach (var (controller, prefix, routes) in controllers)
        {
            Assert.That(((RouteAttribute)controller.GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template, Is.EqualTo(prefix));
            Assert.That(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true), Has.Length.EqualTo(1));
            var actual = controller.GetMethods().SelectMany(method => method.GetCustomAttributes(typeof(HttpPostAttribute), true))
                .Cast<HttpPostAttribute>().Select(attribute => attribute.Template);
            Assert.That(actual, Is.EquivalentTo(routes));
        }
    }
    private static void AssertInvalidResponse(ObjectResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.StatusCode, Is.EqualTo(500));
            Assert.That(Json(result).GetProperty("code").GetString(), Is.EqualTo("InvalidResponse"));
            Assert.That(Json(result).GetProperty("error").GetString(), Is.EqualTo(new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None).Message));
        });
    }
}
