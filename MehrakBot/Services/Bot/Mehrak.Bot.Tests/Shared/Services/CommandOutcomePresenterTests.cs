using Mehrak.Bot.Shared.Extensions;
using Mehrak.Bot.Shared.Models;
using Mehrak.Bot.Shared.Services;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using NetCord;
using NetCord.Rest;

namespace Mehrak.Bot.Tests.Shared.Services;

[TestFixture]
public class CommandOutcomePresenterTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.FromUnixTimeSeconds(1760000000);
    private static readonly DateTimeOffset RecoveryAt = ObservedAt.AddSeconds(123);
    private static readonly DateTimeOffset ResetAt = ObservedAt.AddDays(3);
    private static readonly DateTimeOffset NextResetAt = ResetAt.AddDays(7);

    private static CommandPresentation Present(CommandOutcome outcome) =>
        CommandOutcomePresenter.Present(CommandResult.Success(outcome).ToProto().ToDomain().Outcome, 42);

    private static string[] Text(CommandPresentation presentation) => presentation.Components.SelectMany(x => x switch
    {
        CommandText text => new[] { text.ToFormattedString() },
        CommandSection section => section.Components.Select(t => t.ToFormattedString()),
        _ => []
    }).ToArray();

    [TestCase(Game.Genshin, "<@42>")]
    [TestCase(Game.HonkaiStarRail, "<@42>")]
    [TestCase(Game.ZenlessZoneZero, "### <@42>")]
    [TestCase(Game.HonkaiImpact3, "### <@42>")]
    public void CharacterCards_PublicMentionAndOrderedGallery(Game game, string mention)
    {
        var presentation = Present(CharacterCardsOutcome.Create(game, "uid",
        [
            CharacterCardItem.Generated("a", "Alpha", 1, StorageReference.Attachment("cached.png")),
            CharacterCardItem.Generated("b", "Beta", 2, StorageReference.Attachment("fallback.png"))
        ]));
        Assert.Multiple(() =>
        {
            Assert.That(Text(presentation), Is.EqualTo(new[] { mention }));
            Assert.That(presentation.IsEphemeral, Is.False);
            Assert.That(presentation.EphemeralMessage, Is.Null);
            Assert.That(presentation.Attachments.Select(x => x.StorageFileName), Is.EqualTo(new[] { "cached.png", "fallback.png" }));
            Assert.That(presentation.ToMessage().Components!.OfType<MediaGalleryProperties>().Single().Items.Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public void PartialCharacters_KeepCardsPublicAndItemMessagesPrivate()
    {
        var presentation = Present(CharacterCardsOutcome.Create(Game.Genshin, "uid",
        [
            CharacterCardItem.NotFound("missing"),
            CharacterCardItem.Generated("a", "Alpha", 1, StorageReference.Attachment("hash.png")),
            CharacterCardItem.Failed("b", new(CommandErrorCode.ImageUpdateFailed, CommandErrorTarget.Image), "Beta"),
            CharacterCardItem.NotAttempted("c")
        ], OperationOutcome.Partial));
        Assert.Multiple(() =>
        {
            Assert.That(Text(presentation), Is.EqualTo(new[] { "<@42>" }));
            Assert.That(presentation.IsEphemeral, Is.False);
            Assert.That(presentation.Attachments.Count(), Is.EqualTo(1));
            Assert.That(presentation.EphemeralMessage, Is.EqualTo("Character missing not found. Please check the name and try again\n" +
                "Beta: Unable to update the required images. Please try again later.\nc: Not attempted."));
        });
    }

    [Test]
    public void CharacterLimit_UsesTypedLimitAndStaysPrivate()
    {
        var presentation = Present(CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a", "b"], 4));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "Exceeded the maximum number of characters per request! (Max 4)" }));
        Assert.That(presentation.IsEphemeral, Is.True);
        Assert.That(presentation.Attachments, Is.Empty);
    }

    [TestCase(CharacterCardStatus.NotFound)]
    [TestCase(CharacterCardStatus.Failed)]
    [TestCase(CharacterCardStatus.NotAttempted)]
    public void CharactersWithoutCards_StayPrivate(CharacterCardStatus status)
    {
        var item = status switch
        {
            CharacterCardStatus.NotFound => CharacterCardItem.NotFound("missing"),
            CharacterCardStatus.Failed => CharacterCardItem.Failed("name", new(CommandErrorCode.ApiUnavailable, CommandErrorTarget.CharacterData)),
            _ => CharacterCardItem.NotAttempted("name")
        };
        var presentation = Present(CharacterCardsOutcome.Create(Game.Genshin, "uid", [item], OperationOutcome.Empty));
        Assert.That(presentation.IsEphemeral, Is.True);
        Assert.That(presentation.Attachments, Is.Empty);
    }

    [TestCase(Game.Genshin)]
    [TestCase(Game.HonkaiStarRail)]
    [TestCase(Game.ZenlessZoneZero)]
    public void Roster_UsesContextMentionAndActualStorageKey(Game game)
    {
        var presentation = Present(RosterOutcome.Card(game, "uid", StorageReference.Attachment("actual.png")));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "<@42>" }));
        Assert.That(presentation.Attachments.Single().StorageFileName, Is.EqualTo("actual.png"));
        Assert.That(presentation.IsEphemeral, Is.False);
        Assert.That(Present(RosterOutcome.Empty(game, "uid")).IsEphemeral, Is.True);
    }

    [TestCase(EndgameMode.SpiralAbyss, "Spiral Abyss")]
    [TestCase(EndgameMode.ImaginariumTheater, "Imaginarium Theater")]
    [TestCase(EndgameMode.StygianOnslaught, "Stygian Onslaught")]
    [TestCase(EndgameMode.MemoryOfChaos, "Memory of Chaos")]
    [TestCase(EndgameMode.PureFiction, "Pure Fiction")]
    [TestCase(EndgameMode.ApocalypticShadow, "Apocalyptic Shadow")]
    [TestCase(EndgameMode.AnomalyArbitration, "Anomaly Arbitration")]
    [TestCase(EndgameMode.ShiyuDefense, "Shiyu Defense")]
    [TestCase(EndgameMode.DeadlyAssault, "Deadly Assault")]
    [TestCase(EndgameMode.SimulatedBattleTrial, "Simulated Battle Trial")]
    public void Endgame_ModeSpecificTitleCycleFloorFooterAndEmptyState(EndgameMode mode, string name)
    {
        var floor = mode == EndgameMode.SpiralAbyss ? 12 : (int?)null;
        var suffix = floor.HasValue ? " (Floor 12)" : "";
        var presentation = Present(EndgameOutcome.Card(Game.Genshin, "uid", mode, StorageReference.Attachment("hash.png"),
            mode == EndgameMode.SimulatedBattleTrial ? null : ObservedAt.ToOffset(TimeSpan.FromHours(8)),
            mode == EndgameMode.SimulatedBattleTrial ? null : ResetAt.AddMilliseconds(999), floor));
        var expected = new List<string> { $"### <@42>'s {name} Summary{suffix}" };
        if (mode != EndgameMode.SimulatedBattleTrial) expected.Add($"Cycle start: <t:{ObservedAt.ToUnixTimeSeconds()}:f>\nCycle end: <t:{ResetAt.ToUnixTimeSeconds()}:f>");
        expected.Add("-# " + CommandOutcomePresenter.ApiLimitationFooter);
        Assert.Multiple(() =>
        {
            Assert.That(Text(presentation), Is.EqualTo(expected));
            Assert.That(presentation.IsContainer, Is.True);
            Assert.That(presentation.IsEphemeral, Is.False);
            Assert.That(presentation.Attachments.Single().StorageFileName, Is.EqualTo("hash.png"));
            var empty = Present(EndgameOutcome.Empty(Game.Genshin, "uid", mode, floor));
            Assert.That(Text(empty), Is.EqualTo(new[] { $"No clear records found for {name}{suffix}" }));
            Assert.That(empty.IsEphemeral, Is.True);
            Assert.That(empty.Attachments, Is.Empty);
        });
    }

    [TestCase(EndgameMode.ImaginariumTheater, false, "Imaginarium Theater")]
    [TestCase(EndgameMode.StygianOnslaught, true, "Stygian Onslaught")]
    public void LockedModes_PreserveTheaterPublicAndStygianPrivate(EndgameMode mode, bool isPrivate, string name)
    {
        var presentation = Present(EndgameOutcome.Locked(Game.Genshin, "uid", mode));
        Assert.That(Text(presentation), Is.EqualTo(new[] { $"{name} is not unlocked" }));
        Assert.That(presentation.IsEphemeral, Is.EqualTo(isPrivate));
        Assert.That(presentation.Attachments, Is.Empty);
    }

    private static GenshinNotesOutcome Genshin() => new("123", ObservedAt, new(120, 200, RecoveryAt),
        new(3, 5, ExpeditionOutcome.InProgress, RecoveryAt), new(200, 2400, RecoveryAt), 2, 3, ResetAt, new(true, NextResetAt));

    [Test]
    public void GenshinNotes_ExactSectionsWordingTimesAndStaticIcons()
    {
        var presentation = Present(Genshin());
        Assert.That(Text(presentation), Is.EqualTo(new[]
        {
            "## Real Time Notes for UID: 123", "### Original Resin", "120/200", "-# Recovers <t:1760000123:R>",
            "### Expeditions", "3/5", "-# Completes <t:1760000123:R>",
            "### Serenitea Pot", "200/2400", "-# Recovers <t:1760000123:R>",
            "### Weekly Bosses", "Remaining Resin Discount: 2/3", $"-# Resets <t:{ResetAt.ToUnixTimeSeconds()}:R>",
            "### Parametric Transformer", "Not Claimed!", $"-# Resets <t:{NextResetAt.ToUnixTimeSeconds()}:R>"
        }));
        AssertIcons(presentation, "genshin", "resin", "expedition", "teapot", "weekly", "transformer");
    }

    [TestCase(ExpeditionOutcome.Undispatched, "None Dispatched!", "To be dispatched")]
    [TestCase(ExpeditionOutcome.Completed, "3/5", "All Expeditions Completed")]
    [TestCase(ExpeditionOutcome.Unknown, "3/5", "Unknown")]
    public void GenshinNotes_FullAndExpeditionStates(ExpeditionOutcome state, string value, string footer)
    {
        var presentation = Present(Genshin() with
        {
            Resin = new(200, 200, null),
            HomeCoin = new(2400, 2400, null),
            Expeditions = new(state == ExpeditionOutcome.Undispatched ? 0 : 3, 5, state, null),
            Transformer = null
        });
        Assert.That(Text(presentation), Is.EqualTo(new[]
        {
            "## Real Time Notes for UID: 123", "### Original Resin", "200/200", "-# Already Full!",
            "### Expeditions", value, "-# " + footer,
            "### Serenitea Pot", "Already Full!", "-# To be collected",
            "### Weekly Bosses", "Remaining Resin Discount: 2/3", $"-# Resets <t:{ResetAt.ToUnixTimeSeconds()}:R>"
        }));
        Assert.That(presentation.Attachments.Count(), Is.EqualTo(4));
    }

    [TestCase(true, "Not Claimed!")]
    [TestCase(false, "Claimed!")]
    public void GenshinTransformer_IsReadyIsNotClaimed(bool ready, string expected)
    {
        var presentation = Present(Genshin() with { Transformer = new(ready, ResetAt) });
        Assert.That(Text(presentation)[^2], Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HsrNotes_TypedAlternatingResetInstantsAreNotRecomputed(bool alternate)
    {
        var su = alternate ? NextResetAt : ResetAt;
        var cw = alternate ? ResetAt : NextResetAt;
        var presentation = Present(new HsrNotesOutcome("123", ObservedAt, new(123, 300, RecoveryAt), 1, 3, ResetAt,
            new(500, 14000, su), new(600, 7000, cw)));
        Assert.That(Text(presentation), Is.EqualTo(new[]
        {
            "## Honkai Star Rail Real-Time Notes (UID: 123)", "### Trailblaze Power", "123/300", "-# Recovers <t:1760000123:R>",
            "### Echoes of War", "Claimed 2/3", $"-# Resets <t:{ResetAt.ToUnixTimeSeconds()}:R>",
            "### Simulated Universe", "500/14000", $"-# Resets <t:{su.ToUnixTimeSeconds()}:R>",
            "### Currency Wars", "600/7000", $"-# Resets <t:{cw.ToUnixTimeSeconds()}:R>"
        }));
        AssertIcons(presentation, "hsr", "tbp", "weekly", "rogue", "gridfight");
    }

    [Test]
    public void HsrNotes_FullPowerAndFullyClaimedEchoes()
    {
        var presentation = Present(new HsrNotesOutcome("123", ObservedAt, new(300, 300, null), 0, 3, ResetAt,
            new(14000, 14000, ResetAt), new(7000, 7000, NextResetAt)));
        Assert.That(Text(presentation), Does.Contain("-# Already Full!").And.Contain("Fully Claimed!"));
    }

    private static ZzzNotesOutcome Zzz() => new("123", ObservedAt, new(120, 240, RecoveryAt), new(100, 400, null),
        ScratchCardOutcome.Incomplete, VideoStoreOutcome.WaitingToOpen, new(1, 3, RecoveryAt), new(2, 5, ResetAt),
        new(3, 7, NextResetAt), TempleAdventureOutcome.Dispatchable, TempleCraftingOutcome.Craftable, TempleSalesOutcome.NotSelling);

    [Test]
    public void ZzzNotes_ExactWordingSectionsRefreshFormattingAndIcon()
    {
        var presentation = Present(Zzz());
        Assert.That(Text(presentation), Is.EqualTo(new[]
        {
            "## Zenless Zone Zero Real-Time Notes (UID: 123)", "### Battery Charge", "120/240", "-# Recovers <t:1760000123:R>",
            "### Daily Missions", "Daily Engagement: 100/400", "Scratch Card/Divination: Incomplete", "Video Store Management: Waiting to Open",
            "### Season Missions", "Bounty Commission: 1/3\n-# Refreshes in <t:1760000123:F>",
            $"Ridu Weekly Points: 2/5\n-# Refreshes in <t:{ResetAt.ToUnixTimeSeconds()}:F>",
            $"Omnicoins: 3/7-# Refreshes in <t:{NextResetAt.ToUnixTimeSeconds()}:F>",
            "### Suibian Temple Management", "Adventure: Dispatchable", "Crafting Workshop: Craftable", "Sales Stall: Not Selling"
        }));
        AssertIcons(presentation, "zzz", "battery");
    }

    [Test]
    public void ZzzNotes_UnavailableFactsFullBatteryAndUnknownEnums()
    {
        var presentation = Present(Zzz() with
        {
            Battery = new(240, 240, null),
            BountyCommission = null,
            WeeklyPoints = null,
            Omnicoins = null,
            ScratchCard = ScratchCardOutcome.Unknown,
            VideoStore = VideoStoreOutcome.Unknown,
            Adventure = TempleAdventureOutcome.Unknown,
            Crafting = TempleCraftingOutcome.Unknown,
            Sales = TempleSalesOutcome.Unknown
        });
        Assert.That(Text(presentation), Does.Contain("-# Fully Recovered!").And.Contain("Bounty Commission: -")
            .And.Contain("Ridu Weekly Points: -").And.Contain("Omnicoins: -")
            .And.Contain("Scratch Card/Divination: Unknown").And.Contain("Video Store Management: Unknown")
            .And.Contain("Adventure: Unknown").And.Contain("Crafting Workshop: Unknown").And.Contain("Sales Stall: Unknown"));
    }

    [TestCase(VideoStoreOutcome.WaitingToOpen, "Waiting to Open")]
    [TestCase(VideoStoreOutcome.Open, "Currently Open")]
    [TestCase(VideoStoreOutcome.RevenueAvailable, "Revenue Available")]
    public void ZzzVideoStates(VideoStoreOutcome state, string expected) =>
        Assert.That(Text(Present(Zzz() with { VideoStore = state })), Does.Contain("Video Store Management: " + expected));

    [TestCase(TempleAdventureOutcome.Dispatchable, "Dispatchable")]
    [TestCase(TempleAdventureOutcome.Adventuring, "Adventuring")]
    [TestCase(TempleAdventureOutcome.Completed, "Completed")]
    public void ZzzAdventureStates(TempleAdventureOutcome state, string expected) =>
        Assert.That(Text(Present(Zzz() with { Adventure = state })), Does.Contain("Adventure: " + expected));

    [TestCase(TempleCraftingOutcome.Craftable, "Craftable")]
    [TestCase(TempleCraftingOutcome.Crafting, "Crafting")]
    public void ZzzCraftingStates(TempleCraftingOutcome state, string expected) =>
        Assert.That(Text(Present(Zzz() with { Crafting = state })), Does.Contain("Crafting Workshop: " + expected));

    [TestCase(TempleSalesOutcome.NotSelling, "Not Selling")]
    [TestCase(TempleSalesOutcome.Selling, "Selling")]
    [TestCase(TempleSalesOutcome.OutOfStock, "Out of Stock")]
    public void ZzzSalesStates(TempleSalesOutcome state, string expected) =>
        Assert.That(Text(Present(Zzz() with { Sales = state })), Does.Contain("Sales Stall: " + expected));

    [TestCase(ScratchCardOutcome.Incomplete, "Incomplete")]
    [TestCase(ScratchCardOutcome.Completed, "Completed")]
    public void ZzzScratchStates(ScratchCardOutcome state, string expected) =>
        Assert.That(Text(Present(Zzz() with { ScratchCard = state })), Does.Contain("Scratch Card/Divination: " + expected));

    [TestCase(RedemptionOutcome.Redeemed, "Redeemed Successfully!")]
    [TestCase(RedemptionOutcome.Expired, "Redemption Code Expired")]
    [TestCase(RedemptionOutcome.Invalid, "Invalid Code")]
    [TestCase(RedemptionOutcome.Cooldown, "Redemption in Cooldown")]
    [TestCase(RedemptionOutcome.AlreadyUsed, "Redemption Code Already Used")]
    [TestCase(RedemptionOutcome.Failed, "An error occurred while redeeming the code")]
    [TestCase(RedemptionOutcome.NotAttempted, "Not attempted.")]
    public void Codes_UseSemanticSafePhrasesAndStayPrivate(RedemptionOutcome state, string expected)
    {
        var item = state == RedemptionOutcome.Failed ? RedemptionItem.Failed("CODE", new(CommandErrorCode.ApiUnavailable, CommandErrorTarget.CodeRedemption))
            : state == RedemptionOutcome.NotAttempted ? RedemptionItem.NotAttempted("CODE") : RedemptionItem.Completed("CODE", state);
        var presentation = Present(CodeRedemptionOutcome.Create(Game.Genshin, "uid", [item]));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "CODE: " + expected }));
        Assert.That(presentation.IsEphemeral, Is.True);
    }

    [Test]
    public void Codes_EmptyAndItemOrdering()
    {
        Assert.That(Text(Present(CodeRedemptionOutcome.Empty(Game.Genshin, "uid"))), Is.EqualTo(new[]
            { "No known codes found in database. Please provide a valid code" }));
        var presentation = Present(CodeRedemptionOutcome.Create(Game.HonkaiStarRail, "uid",
            [RedemptionItem.Completed("B", RedemptionOutcome.Invalid), RedemptionItem.Completed("A", RedemptionOutcome.Redeemed)]));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "B: Invalid Code\nA: Redeemed Successfully!" }));
    }

    [TestCase(CheckInOutcome.Succeeded, "Check-in successful!")]
    [TestCase(CheckInOutcome.AlreadyCheckedIn, "Already checked in today.")]
    [TestCase(CheckInOutcome.NoValidProfile, "No valid account found.")]
    [TestCase(CheckInOutcome.Unknown, "Unknown status.")]
    [TestCase(CheckInOutcome.NotAttempted, "Not attempted.")]
    public void CheckIn_SafeStatesArePrivate(CheckInOutcome state, string expected)
    {
        var item = state == CheckInOutcome.NotAttempted ? CheckInItem.NotAttempted(Game.Genshin) : CheckInItem.Completed(Game.Genshin, state);
        var presentation = Present(DailyCheckInOutcome.Create([item]));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "Genshin Impact: " + expected }));
        Assert.That(presentation.IsEphemeral, Is.True);
    }

    [Test]
    public void CheckIn_EmptySameDayGameLabelsOrderAndFailures()
    {
        Assert.That(Text(Present(DailyCheckInOutcome.Empty())), Is.EqualTo(new[] { "No game records found." }));
        Assert.That(Text(Present(DailyCheckInOutcome.AlreadyCompleted())), Is.EqualTo(new[] { "You have already checked in today" }));
        var presentation = Present(DailyCheckInOutcome.Create(
        [
            CheckInItem.Completed(Game.HonkaiStarRail, CheckInOutcome.Succeeded),
            CheckInItem.Completed(Game.ZenlessZoneZero, CheckInOutcome.AlreadyCheckedIn),
            CheckInItem.Completed(Game.HonkaiImpact3, CheckInOutcome.NoValidProfile),
            CheckInItem.Completed(Game.TearsOfThemis, CheckInOutcome.Unknown),
            CheckInItem.Completed(Game.Unsupported, CheckInOutcome.Unknown),
            CheckInItem.Failed(Game.Genshin, new(CommandErrorCode.Timeout, CommandErrorTarget.DailyCheckIn)),
            CheckInItem.Failed(Game.Genshin, new(CommandErrorCode.ApiUnavailable, CommandErrorTarget.DailyCheckIn))
        ], OperationOutcome.Partial));
        Assert.That(Text(presentation), Is.EqualTo(new[] { "Honkai: Star Rail: Check-in successful!\n" +
            "Zenless Zone Zero: Already checked in today.\nHonkai Impact 3rd: No valid account found.\nTears of Themis: Unknown status.\n" +
            "Unsupported: Unknown status.\nGenshin Impact: Check-in timed out.\nGenshin Impact: Unable to retrieve game data. Please try again later." }));
        Assert.That(presentation.IsEphemeral, Is.True);
    }

    [TestCase(CommandErrorCode.Unknown)]
    [TestCase(CommandErrorCode.AuthenticationRequired)]
    [TestCase(CommandErrorCode.ApiUnavailable)]
    [TestCase(CommandErrorCode.ImageUpdateFailed)]
    [TestCase(CommandErrorCode.AttachmentStoreFailed)]
    [TestCase(CommandErrorCode.Cancelled)]
    [TestCase(CommandErrorCode.Timeout)]
    [TestCase(CommandErrorCode.InvalidRequest)]
    [TestCase(CommandErrorCode.InvalidResponse)]
    [TestCase(CommandErrorCode.CharacterNotFound)]
    [TestCase(CommandErrorCode.RequestLimitExceeded)]
    public void EnvelopeErrors_UseOnlyAllowlistedMessage(CommandErrorCode code)
    {
        var error = new CommandError(code, CommandErrorTarget.GameProfile);
        Assert.That(CommandOutcomePresenter.SafeErrorText(error), Is.EqualTo(error.Message));
    }

    [Test]
    public void MissingOutcomeAndError_AreSafePrivateResponses()
    {
        var presentation = CommandOutcomePresenter.Present(null, 42);
        Assert.That(presentation.IsEphemeral, Is.True);
        Assert.That(Text(presentation), Is.EqualTo(new[] { new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None).Message }));
        Assert.That(CommandOutcomePresenter.SafeErrorText(null), Is.EqualTo(Text(presentation).Single()));
    }

    private static void AssertIcons(CommandPresentation presentation, string game, params string[] icons)
    {
        Assert.Multiple(() =>
        {
            Assert.That(presentation.IsEphemeral, Is.True);
            Assert.That(presentation.IsContainer, Is.True);
            Assert.That(presentation.Attachments.Select(x => x.FileName), Is.EqualTo(icons.Select(x => $"{game}_{x}.png")));
            Assert.That(presentation.Attachments.Select(x => x.StorageFileName), Is.EqualTo(icons.Select(x => $"{game}/{x}.png")));
            Assert.That(presentation.Attachments.Select(x => x.SourceType), Is.All.EqualTo(AttachmentSourceType.ImageStorage));
            var message = presentation.ToMessage();
            Assert.That(message.Flags, Is.EqualTo(MessageFlags.IsComponentsV2));
            var container = message.Components!.OfType<ComponentContainerProperties>().Single();
            Assert.That(container.Components.OfType<ComponentSectionProperties>().Count(), Is.EqualTo(icons.Length));
        });
    }
}
