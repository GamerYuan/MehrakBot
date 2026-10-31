using Google.Protobuf;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Domain.Tests.Command;

[TestFixture]
public class CommandResultMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly StorageReference Attachment = StorageReference.Attachment("cards/sha256.jpg");
    private static readonly CommandError ApiError = new(CommandErrorCode.ApiUnavailable, CommandErrorTarget.CharacterData);

    private static CommandResult Roundtrip(CommandResult result)
    {
        var wire = result.ToProto();
        var mapped = Proto.CommandResult.Parser.ParseFrom(wire.ToByteArray()).ToDomain();
        Assert.That(mapped.ToProto().ToByteArray(), Is.EqualTo(wire.ToByteArray()), "Every field must survive serialization.");
        return mapped;
    }

    public static IEnumerable<CommandOutcome> AllFamilies()
    {
        yield return CharacterCardsOutcome.Create(Game.Genshin, "123", [
            CharacterCardItem.Generated("alias", "Character", 42, Attachment),
            CharacterCardItem.NotFound("missing"),
            CharacterCardItem.Failed("failed", ApiError, "Failed Character", 43),
            CharacterCardItem.NotAttempted("later", "Later Character", 44)
        ], OperationOutcome.Partial);
        yield return RosterOutcome.Card(Game.HonkaiStarRail, "456", StorageReference.Image("hsr/roster.png"));
        yield return EndgameOutcome.Card(Game.Genshin, "123", EndgameMode.SpiralAbyss, Attachment, Now, Now.AddDays(14), 12);
        yield return new GenshinNotesOutcome("123", Now,
            new(10, 200, Now.AddHours(2)), new(3, 5, ExpeditionOutcome.InProgress, Now.AddHours(4)),
            new(100, 2400, Now.AddDays(1)), 2, 3, Now.AddDays(5), new(true, Now.AddDays(5)));
        yield return new HsrNotesOutcome("456", Now, new(100, 300, Now.AddHours(1)), 1, 3, Now.AddDays(5),
            new(1000, 14000, Now.AddDays(5)), new(200, 3000, Now.AddDays(12)));
        yield return new ZzzNotesOutcome("789", Now, new(10, 240, Now.AddHours(1)), new(100, 400, null),
            ScratchCardOutcome.Completed, VideoStoreOutcome.RevenueAvailable, new(1, 3, Now.AddDays(3)),
            new(300, 1000, Now.AddDays(2)), new(100, 200, Now.AddDays(1)),
            TempleAdventureOutcome.Adventuring, TempleCraftingOutcome.Craftable, TempleSalesOutcome.OutOfStock);
        yield return CodeRedemptionOutcome.Create(Game.ZenlessZoneZero, "789", [
            RedemptionItem.Completed("SUCCESS", RedemptionOutcome.Redeemed),
            RedemptionItem.Completed("EXPIRED", RedemptionOutcome.Expired),
            RedemptionItem.Completed("INVALID", RedemptionOutcome.Invalid),
            RedemptionItem.Completed("COOLDOWN", RedemptionOutcome.Cooldown),
            RedemptionItem.Completed("USED", RedemptionOutcome.AlreadyUsed),
            RedemptionItem.Failed("FAILED", new(CommandErrorCode.Timeout, CommandErrorTarget.CodeRedemption)),
            RedemptionItem.NotAttempted("LATER")
        ], OperationOutcome.Partial);
        yield return DailyCheckInOutcome.Create([
            CheckInItem.Completed(Game.Genshin, CheckInOutcome.Succeeded),
            CheckInItem.Completed(Game.HonkaiStarRail, CheckInOutcome.AlreadyCheckedIn),
            CheckInItem.Completed(Game.ZenlessZoneZero, CheckInOutcome.NoValidProfile),
            CheckInItem.Completed(Game.HonkaiImpact3, CheckInOutcome.Unknown),
            CheckInItem.Failed(Game.TearsOfThemis, new(CommandErrorCode.Timeout, CommandErrorTarget.DailyCheckIn)),
            CheckInItem.NotAttempted(Game.Unsupported)
        ], OperationOutcome.Partial);
    }

    [TestCaseSource(nameof(AllFamilies))]
    public void EveryFamily_SuccessAndFailedEnvelope_RoundtripsEveryField(CommandOutcome outcome)
    {
        var success = Roundtrip(CommandResult.Success(outcome));
        Assert.That(success.IsSuccess, Is.True);
        Assert.That(success.Outcome, Is.TypeOf(outcome.GetType()));
        Assert.That(success.Error, Is.Null);
        var failure = Roundtrip(CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout,
            CommandErrorTarget.None, outcome));
        Assert.That(failure.IsSuccess, Is.False);
        Assert.That(failure.Outcome, Is.TypeOf(outcome.GetType()));
        Assert.That(failure.FailureReason, Is.EqualTo(CommandFailureReason.Timeout));
        Assert.That(failure.Error!.Code, Is.EqualTo(CommandErrorCode.Timeout));
    }

    [Test]
    public void CharacterPartial_ContainsOrderedCompletedFailedAndNotAttemptedItems()
    {
        var outcome = (CharacterCardsOutcome)AllFamilies().First();
        var actual = (CharacterCardsOutcome)Roundtrip(CommandResult.Success(outcome)).Outcome!;
        Assert.That(actual.Items, Is.EqualTo(outcome.Items));
        Assert.That(actual.State, Is.EqualTo(OperationOutcome.Partial));
    }

    [Test]
    public void LimitExceeded_RetainsLimitAndAllRequestedNamesWithoutProfile()
    {
        var actual = (CharacterCardsOutcome)Roundtrip(CommandResult.Success(
            CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a", "b", "c", "d", "e"], 4))).Outcome!;
        Assert.Multiple(() =>
        {
            Assert.That(actual.RequestLimit, Is.EqualTo(4));
            Assert.That(actual.GameUid, Is.Null);
            Assert.That(actual.State, Is.EqualTo(OperationOutcome.RequestLimitExceeded));
            Assert.That(actual.Items.Select(x => x.RequestedName), Is.EqualTo(new[] { "a", "b", "c", "d", "e" }));
            Assert.That(actual.Items.All(x => x.Status == CharacterCardStatus.NotAttempted), Is.True);
        });
    }

    [Test]
    public void EmptyAndLockedStates_RoundtripWithoutInventingImages()
    {
        CommandOutcome[] outcomes = [
            CharacterCardsOutcome.Create(Game.Genshin, "123", [], OperationOutcome.Empty),
            RosterOutcome.Empty(Game.Genshin, "123"),
            EndgameOutcome.Empty(Game.Genshin, "123", EndgameMode.SpiralAbyss, 12),
            EndgameOutcome.Locked(Game.Genshin, "123", EndgameMode.ImaginariumTheater),
            CodeRedemptionOutcome.Empty(Game.Genshin, "123"), DailyCheckInOutcome.Empty(), DailyCheckInOutcome.AlreadyCompleted()
        ];
        foreach (var outcome in outcomes) Assert.That(Roundtrip(CommandResult.Success(outcome)).IsSuccess, Is.True);
    }

    [Test]
    public void FailedOperationState_RoundtripsIndependentlyOfEnvelopeSuccess()
    {
        var outcome = CharacterCardsOutcome.Create(Game.Genshin, "123", [CharacterCardItem.Failed("a", ApiError)], OperationOutcome.Failed);
        var actual = (CharacterCardsOutcome)Roundtrip(CommandResult.Failure(CommandFailureReason.ApiError, ApiError, outcome)).Outcome!;
        Assert.That(actual.State, Is.EqualTo(OperationOutcome.Failed));
        Assert.That(actual.Items, Is.EqualTo(outcome.Items));
    }

    [Test]
    public void FailureWithoutPayload_AllReasonsCodesAndTargetsRemainTypedAndSafe()
    {
        foreach (var reason in System.Enum.GetValues<CommandFailureReason>())
            foreach (var code in System.Enum.GetValues<CommandErrorCode>())
                foreach (var target in System.Enum.GetValues<CommandErrorTarget>())
                {
                    var actual = Roundtrip(CommandResult.Failure(reason, code, target));
                    Assert.That(actual.FailureReason, Is.EqualTo(reason));
                    Assert.That(actual.Error, Is.EqualTo(new CommandError(code, target)));
                    Assert.That(actual.Error!.Message, Is.Not.Empty);
                    Assert.That(actual.Outcome, Is.Null);
                }
        Assert.That(Proto.CommandError.Descriptor.Fields.InFieldNumberOrder().Select(x => x.Name),
            Is.EqualTo(new[] { "code", "target" }), "No arbitrary error string belongs on the wire.");
    }

    [Test]
    public void EveryGame_UsesExplicitNonMatchingWireNumbers()
    {
        foreach (var game in System.Enum.GetValues<Game>())
        {
            var wire = CommandResult.Success(RosterOutcome.Empty(game, "123")).ToProto();
            Assert.That(((RosterOutcome)wire.ToDomain().Outcome!).Game, Is.EqualTo(game));
            if (game != Game.Unsupported) Assert.That((int)wire.Roster.Game, Is.Not.EqualTo((int)game));
        }
    }

    [Test]
    public void EveryEndgameMode_RoundtripsCycleAndOptionalFloor()
    {
        foreach (var mode in System.Enum.GetValues<EndgameMode>().Where(x => x != EndgameMode.Unknown))
            Assert.That(((EndgameOutcome)Roundtrip(CommandResult.Success(
                EndgameOutcome.Card(Game.HonkaiStarRail, "123", mode, Attachment, Now, Now.AddDays(1)))).Outcome!).Mode,
                Is.EqualTo(mode));
    }

    [Test]
    public void OptionalScalarPresence_DistinguishesMissingFromExplicitEmptyAndZero()
    {
        var outcome = CharacterCardsOutcome.Create(Game.Genshin, "", [
            new("a", "", 0, CharacterCardStatus.NotAttempted, null, null),
            CharacterCardItem.NotAttempted("b")
        ], OperationOutcome.Partial, 0);
        var actual = (CharacterCardsOutcome)Roundtrip(CommandResult.Success(outcome)).Outcome!;
        Assert.Multiple(() =>
        {
            Assert.That(actual.GameUid, Is.EqualTo(""));
            Assert.That(actual.RequestLimit, Is.Zero);
            Assert.That(actual.Items[0].CharacterName, Is.EqualTo(""));
            Assert.That(actual.Items[0].CharacterId, Is.Zero);
            Assert.That(actual.Items[1].CharacterName, Is.Null);
            Assert.That(actual.Items[1].CharacterId, Is.Null);
        });
        var endgame = EndgameOutcome.Empty(Game.Genshin, "123", EndgameMode.SpiralAbyss, 0);
        Assert.That(((EndgameOutcome)Roundtrip(CommandResult.Success(endgame)).Outcome!).Floor, Is.Zero);
    }

    [Test]
    public void MissingNotesFacts_RemainAbsentAndFullResourcesHaveNoRecoveryInstant()
    {
        var zzz = new ZzzNotesOutcome("123", Now, new(240, 240, null), new(400, 400, null),
            ScratchCardOutcome.Unknown, VideoStoreOutcome.Unknown, null, new(0, 0, null), null,
            TempleAdventureOutcome.Unknown, TempleCraftingOutcome.Unknown, TempleSalesOutcome.Unknown);
        var actual = (ZzzNotesOutcome)Roundtrip(CommandResult.Success(zzz)).Outcome!;
        Assert.Multiple(() =>
        {
            Assert.That(actual.Battery.RecoveryAt, Is.Null);
            Assert.That(actual.BountyCommission, Is.Null);
            Assert.That(actual.WeeklyPoints, Is.EqualTo(new NotesCounter(0, 0, null)));
            Assert.That(actual.Omnicoins, Is.Null);
        });
        var genshin = new GenshinNotesOutcome("123", Now, new(200, 200, null),
            new(0, 5, ExpeditionOutcome.Undispatched, null), new(2400, 2400, null), 3, 3, Now.AddDays(1), null);
        Assert.That(((GenshinNotesOutcome)Roundtrip(CommandResult.Success(genshin)).Outcome!).Transformer, Is.Null);
    }

    [Test]
    public void EveryNotesState_Roundtrips()
    {
        foreach (var state in System.Enum.GetValues<ExpeditionOutcome>())
            Assert.That(new ExpeditionNotes(1, 5, state, Now).ToProto().ToDomain().State, Is.EqualTo(state));
        foreach (var scratch in System.Enum.GetValues<ScratchCardOutcome>())
            foreach (var video in System.Enum.GetValues<VideoStoreOutcome>())
                foreach (var adventure in System.Enum.GetValues<TempleAdventureOutcome>())
                    foreach (var crafting in System.Enum.GetValues<TempleCraftingOutcome>())
                        foreach (var sales in System.Enum.GetValues<TempleSalesOutcome>())
                        {
                            var outcome = new ZzzNotesOutcome("123", Now, new(1, 240, Now), new(1, 400, null),
                                scratch, video, null, null, null, adventure, crafting, sales);
                            Assert.That(Roundtrip(CommandResult.Success(outcome)).Outcome, Is.EqualTo(outcome));
                        }
    }

    [Test]
    public void Timestamps_NormalizeOffsetsAndPreserveSubsecondPrecision()
    {
        var local = Now.ToOffset(TimeSpan.FromHours(8)).AddTicks(1234567);
        var outcome = EndgameOutcome.Card(Game.Genshin, "123", EndgameMode.SpiralAbyss, Attachment, local, local.AddDays(1));
        var actual = (EndgameOutcome)Roundtrip(CommandResult.Success(outcome)).Outcome!;
        Assert.Multiple(() =>
        {
            Assert.That(outcome.CycleStart!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(actual.CycleStart, Is.EqualTo(local));
            Assert.That(actual.CycleStart!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(actual.CycleEnd, Is.EqualTo(local.AddDays(1)));
            Assert.That(new NotesResource(1, 2, local).RecoveryAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(new NotesCounter(1, 2, local).ResetAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(new ExpeditionNotes(1, 2, ExpeditionOutcome.InProgress, local).CompletionAt!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(new TransformerNotes(true, local).ResetAt.Offset, Is.EqualTo(TimeSpan.Zero));
        });
        var genshin = (GenshinNotesOutcome)AllFamilies().ElementAt(3);
        Assert.That((genshin with { }).ObservedAt.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    public static IEnumerable<CommandOutcome> ContradictoryStatusPayloads()
    {
        yield return RosterOutcome.Empty(Game.Genshin, "123") with { Image = Attachment };
        yield return EndgameOutcome.Empty(Game.Genshin, "123", EndgameMode.SpiralAbyss) with { Image = Attachment };
        yield return EndgameOutcome.Locked(Game.Genshin, "123", EndgameMode.ImaginariumTheater) with { Image = Attachment };
        foreach (var status in new[] { CharacterCardStatus.NotFound, CharacterCardStatus.Failed, CharacterCardStatus.NotAttempted })
            yield return CharacterCardsOutcome.Create(Game.Genshin, "123", [
                new("a", "A", 1, status, Attachment, status == CharacterCardStatus.Failed ? ApiError : null)
            ]);
        yield return CharacterCardsOutcome.Create(Game.Genshin, "123", [
            CharacterCardItem.Generated("a", "A", 1, Attachment) with { Error = ApiError }
        ]);
        yield return CharacterCardsOutcome.Create(Game.Genshin, "123", [
            CharacterCardItem.NotAttempted("a") with { Error = ApiError }
        ]);
        yield return CharacterCardsOutcome.Create(Game.Genshin, "123", [
            CharacterCardItem.Failed("a", ApiError) with { Error = null }
        ]);
        foreach (var status in System.Enum.GetValues<RedemptionOutcome>().Where(x => x is not (RedemptionOutcome.Unknown or RedemptionOutcome.Failed)))
            yield return CodeRedemptionOutcome.Create(Game.Genshin, "123", [new("CODE", status, ApiError)]);
        yield return CodeRedemptionOutcome.Create(Game.Genshin, "123", [
            RedemptionItem.Failed("CODE", ApiError) with { Error = null }
        ]);
        foreach (var status in System.Enum.GetValues<CheckInOutcome>().Where(x => x != CheckInOutcome.Failed))
            yield return DailyCheckInOutcome.Create([new(Game.Genshin, status, ApiError)]);
        yield return DailyCheckInOutcome.Create([CheckInItem.Failed(Game.Genshin, ApiError) with { Error = null }]);
    }

    [TestCaseSource(nameof(ContradictoryStatusPayloads))]
    public void ContradictoryStatusFields_SerializedWireReturnsSafeInvalidResponse(CommandOutcome outcome)
    {
        // Bypass envelope validation to simulate a malformed producer on the wire.
        var wire = new Proto.CommandResult();
        switch (outcome)
        {
            case CharacterCardsOutcome cards: wire.CharacterCards = cards.ToProto(); break;
            case RosterOutcome roster: wire.Roster = roster.ToProto(); break;
            case EndgameOutcome endgame: wire.Endgame = endgame.ToProto(); break;
            case CodeRedemptionOutcome redemption: wire.CodeRedemption = redemption.ToProto(); break;
            case DailyCheckInOutcome checkIn: wire.DailyCheckIn = checkIn.ToProto(); break;
            default: throw new ArgumentException("Unsupported test outcome.", nameof(outcome));
        }
        foreach (var success in new[] { true, false })
        {
            wire.IsSuccess = success;
            wire.FailureReason = success ? Proto.CommandFailureReason.Unknown : Proto.CommandFailureReason.Timeout;
            wire.Error = success ? null : new CommandError(CommandErrorCode.Timeout, CommandErrorTarget.None).ToProto();
            var result = Proto.CommandResult.Parser.ParseFrom(wire.ToByteArray()).ToDomain();
            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
                Assert.That(result.Error, Is.EqualTo(new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None)));
                Assert.That(result.Outcome, Is.Null);
            });
        }
    }

    [TestCaseSource(nameof(ContradictoryStatusPayloads))]
    public void ContradictoryStatusFields_DomainSerializationRejectsPayload(CommandOutcome outcome)
    {
        Assert.That(() => CommandResult.Success(outcome).ToProto(), Throws.ArgumentException);
        Assert.That(() => CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout,
            outcome: outcome).ToProto(), Throws.ArgumentException);
    }

    [Test]
    public void ValidStatusFields_RoundtripWithNotFoundErrorAndUnknownCheckInPreserved()
    {
        var outcomes = AllFamilies().Concat([
            CharacterCardsOutcome.Create(Game.Genshin, "123", [CharacterCardItem.NotFound("missing") with { Error = null }])
        ]);
        foreach (var outcome in outcomes)
        {
            var success = Roundtrip(CommandResult.Success(outcome));
            var failure = Roundtrip(CommandResult.Failure(CommandFailureReason.Timeout, CommandErrorCode.Timeout, outcome: outcome));
            Assert.That(success.IsSuccess, Is.True);
            Assert.That(failure.Error!.Code, Is.EqualTo(CommandErrorCode.Timeout));
            Assert.That(failure.Outcome, Is.Not.Null);
        }
        var cards = (CharacterCardsOutcome)Roundtrip(CommandResult.Success(AllFamilies().First())).Outcome!;
        Assert.That(cards.Items[1].Error, Is.EqualTo(new CommandError(CommandErrorCode.CharacterNotFound, CommandErrorTarget.CharacterData)));
        var checkIn = (DailyCheckInOutcome)Roundtrip(CommandResult.Success(AllFamilies().Last())).Outcome!;
        Assert.That(checkIn.Items[3].Outcome, Is.EqualTo(CheckInOutcome.Unknown));
        Assert.That(checkIn.Items[3].Error, Is.Null);
    }

    public static IEnumerable<Proto.CommandResult> MalformedResults()
    {
        yield return new() { IsSuccess = true };
        yield return new() { IsSuccess = false };
        yield return new() { IsSuccess = false, FailureReason = (Proto.CommandFailureReason)999, Error = new() };
        yield return new() { IsSuccess = false, Error = new() { Code = (Proto.CommandErrorCode)999 } };
        yield return new() { IsSuccess = false, Error = new() { Target = (Proto.CommandErrorTarget)999 } };
        var good = CommandResult.Success(RosterOutcome.Empty(Game.Genshin, "123")).ToProto();
        var badGame = good.Clone(); badGame.Roster.Game = (Proto.CommandGame)999; yield return badGame;
        var badState = good.Clone(); badState.Roster.State = (Proto.OperationOutcome)999; yield return badState;
        var missingState = good.Clone(); missingState.Roster.State = Proto.OperationOutcome.Unknown; yield return missingState;
        var badSuccess = good.Clone(); badSuccess.Error = new(); yield return badSuccess;
        var badReason = good.Clone(); badReason.FailureReason = Proto.CommandFailureReason.Timeout; yield return badReason;
        var missingImage = good.Clone(); missingImage.Roster.State = Proto.OperationOutcome.Complete; yield return missingImage;
        var badStorage = good.Clone(); badStorage.Roster.Image = new() { SourceType = (Proto.AttachmentSourceType)999, StorageFileName = "a" }; yield return badStorage;
        var emptyStorage = good.Clone(); emptyStorage.Roster.Image = new() { SourceType = Proto.AttachmentSourceType.AttachmentStorage }; yield return emptyStorage;
        var unknownStorage = good.Clone(); unknownStorage.Roster.Image = new() { StorageFileName = "a" }; yield return unknownStorage;
        var notes = CommandResult.Success(AllFamilies().ElementAt(3)).ToProto();
        var missingTimestamp = notes.Clone(); missingTimestamp.GenshinNotes.ObservedAt = null; yield return missingTimestamp;
        var missingResource = notes.Clone(); missingResource.GenshinNotes.Resin = null; yield return missingResource;
        var invalidTimestamp = notes.Clone(); invalidTimestamp.GenshinNotes.ObservedAt = new() { Seconds = long.MaxValue }; yield return invalidTimestamp;
        var invalidNanos = notes.Clone(); invalidNanos.GenshinNotes.Resin.RecoveryAt = new() { Nanos = -1 }; yield return invalidNanos;
        var invalidEnum = notes.Clone(); invalidEnum.GenshinNotes.Expeditions.State = (Proto.ExpeditionOutcome)999; yield return invalidEnum;
        var invalidCharacter = CommandResult.Success(AllFamilies().First()).ToProto();
        invalidCharacter.CharacterCards.Items[0].Image = null; yield return invalidCharacter;
        var invalidRedemption = CommandResult.Success(AllFamilies().ElementAt(6)).ToProto();
        invalidRedemption.CodeRedemption.Items[0].Outcome = (Proto.RedemptionOutcome)999; yield return invalidRedemption;
        var missingItemError = CommandResult.Success(AllFamilies().ElementAt(7)).ToProto();
        missingItemError.DailyCheckIn.Items[4].Error = null; yield return missingItemError;
    }

    [TestCaseSource(nameof(MalformedResults))]
    public void MalformedOrUnknownWireData_ReturnsSafeInvalidResponse(Proto.CommandResult wire)
    {
        var result = Proto.CommandResult.Parser.ParseFrom(wire.ToByteArray()).ToDomain();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(CommandFailureReason.BotError));
            Assert.That(result.Error!.Code, Is.EqualTo(CommandErrorCode.InvalidResponse));
            Assert.That(result.Outcome, Is.Null);
        });
    }

    [Test]
    public void UnknownFuturePayloadAndRetiredPresentationFields_AreNotTreatedAsSuccess()
    {
        // is_success=true, unknown length-delimited payload at tag 18.
        var future = Proto.CommandResult.Parser.ParseFrom(new byte[] { 8, 1, 146, 1, 0 }).ToDomain();
        Assert.That(future.Error!.Code, Is.EqualTo(CommandErrorCode.InvalidResponse));
        // Old error_message (tag 2) cannot leak text through the new error object.
        var retired = Proto.CommandResult.Parser.ParseFrom(new byte[] { 18, 3, 98, 97, 100 }).ToDomain();
        Assert.That(retired.Error!.Code, Is.EqualTo(CommandErrorCode.InvalidResponse));
        Assert.That(retired.Error.Message, Does.Not.Contain("bad"));
        var fields = Proto.CommandResult.Descriptor.Fields.InFieldNumberOrder();
        Assert.That(fields.Any(x => x.FieldNumber is 2 or 4), Is.False);
        Assert.That(fields.Any(x => x.Name is "data" or "error_message"), Is.False);
        var descriptor = Proto.CommandResult.Descriptor.ToProto();
        Assert.That(descriptor.ReservedName, Is.EquivalentTo(new[] { "error_message", "data" }));
        Assert.That(descriptor.ReservedRange.Any(x => x.Start <= 2 && x.End > 2), Is.True);
        Assert.That(descriptor.ReservedRange.Any(x => x.Start <= 4 && x.End > 4), Is.True);
    }

    [Test]
    public void InvalidDomainPayload_IsRejectedInsteadOfSilentlyDroppingItems()
    {
        var invalid = CharacterCardsOutcome.Create(Game.Genshin, "123", [
            new("a", "A", 1, CharacterCardStatus.Generated, null, null)
        ]);
        Assert.That(() => CommandResult.Success(invalid).ToProto(), Throws.ArgumentException);
        Assert.That(() => CommandResult.Success(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void RequestSerialization_RemainsUnchanged()
    {
        var request = ProtobufMappingExtensions.ToExecuteRequest("genshin.character", 1, 2, "synthetic-token",
            [("character", "A"), ("server", Server.Asia), ("floor", 12), ("empty", null!)]);
        var actual = Proto.ExecuteRequest.Parser.ParseFrom(request.ToByteArray());
        Assert.Multiple(() =>
        {
            Assert.That(actual.CommandName, Is.EqualTo("genshin.character"));
            Assert.That(actual.DiscordUserId, Is.EqualTo(1));
            Assert.That(actual.LtUid, Is.EqualTo(2));
            Assert.That(actual.LToken, Is.EqualTo("synthetic-token"));
            Assert.That(actual.Parameters["character"], Is.EqualTo("A"));
            Assert.That(actual.Parameters["server"], Is.EqualTo("Asia"));
            Assert.That(actual.Parameters["floor"], Is.EqualTo("12"));
            Assert.That(actual.Parameters["empty"], Is.Empty);
        });
    }
}
