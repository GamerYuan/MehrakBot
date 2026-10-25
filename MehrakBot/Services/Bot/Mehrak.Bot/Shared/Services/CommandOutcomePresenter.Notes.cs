using System.Globalization;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Models;
using Mehrak.Domain.Command.Models;
using static Mehrak.Bot.Shared.Models.CommandText;

namespace Mehrak.Bot.Shared.Services;

internal static partial class CommandOutcomePresenter
{
    private static CommandSection Section(string title, string value, string footer, string game, string icon) =>
        new([new CommandText(title, TextType.Header3), new CommandText(value), new CommandText(footer, TextType.Footer)],
            new CommandAttachment($"{game}_{icon}.png", AttachmentSourceType.ImageStorage, $"{game}/{icon}.png"));

    private static string Count(NotesResource resource) => $"{resource.Current}/{resource.Maximum}";
    private static string Count(NotesCounter counter) => $"{counter.Current}/{counter.Maximum}";

    private static string Reset(DateTimeOffset? time, ICommandUiLocalizer localizer, CultureInfo culture) =>
        time is { } reset ? localizer.Get(culture, CommandUiKeys.NotesReset, Timestamp(reset, 'R')) : "-";

    private static string Recovery(NotesResource resource, string fullText, ICommandUiLocalizer localizer,
        CultureInfo culture) =>
        resource.RecoveryAt is { } time
            ? localizer.Get(culture, CommandUiKeys.NotesRecovers, Timestamp(time, 'R'))
            : fullText;

    private static CommandPresentation PresentGenshinNotes(GenshinNotesOutcome notes, ICommandUiLocalizer localizer,
        CultureInfo culture)
    {
        var expeditionValue = notes.Expeditions.State == ExpeditionOutcome.Undispatched
            ? localizer.Get(culture, CommandUiKeys.NotesGenshinExpeditionNone)
            : $"{notes.Expeditions.Current}/{notes.Expeditions.Maximum}";
        var expeditionFooter = notes.Expeditions.State switch
        {
            ExpeditionOutcome.Undispatched => localizer.Get(culture, CommandUiKeys.NotesGenshinToBeDispatched),
            ExpeditionOutcome.Completed => localizer.Get(culture, CommandUiKeys.NotesGenshinAllCompleted),
            ExpeditionOutcome.InProgress when notes.Expeditions.CompletionAt is { } time =>
                localizer.Get(culture, CommandUiKeys.NotesGenshinCompletes, Timestamp(time, 'R')),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        };
        var alreadyFull = localizer.Get(culture, CommandUiKeys.NotesGenshinAlreadyFull);
        List<IPresentationComponent> components =
        [
            new CommandText(localizer.Get(culture, CommandUiKeys.NotesGenshinTitle, notes.GameUid), TextType.Header2),
            Section(localizer.Get(culture, CommandUiKeys.NotesGenshinResin), Count(notes.Resin),
                Recovery(notes.Resin, alreadyFull, localizer, culture), "genshin", "resin"),
            Section(localizer.Get(culture, CommandUiKeys.NotesGenshinExpeditions), expeditionValue, expeditionFooter,
                "genshin", "expedition"),
            Section(localizer.Get(culture, CommandUiKeys.NotesGenshinSereniteaPot),
                notes.HomeCoin.RecoveryAt == null ? alreadyFull : Count(notes.HomeCoin),
                Recovery(notes.HomeCoin, localizer.Get(culture, CommandUiKeys.NotesGenshinToBeCollected), localizer,
                    culture), "genshin", "teapot"),
            Section(localizer.Get(culture, CommandUiKeys.NotesGenshinWeeklyBosses),
                localizer.Get(culture, CommandUiKeys.NotesGenshinWeeklyDiscount, notes.RemainingWeeklyDiscounts,
                    notes.WeeklyDiscountLimit),
                Reset(notes.WeeklyResetAt, localizer, culture), "genshin", "weekly")
        ];
        if (notes.Transformer is { } transformer)
            components.Add(Section(localizer.Get(culture, CommandUiKeys.NotesGenshinTransformer),
                transformer.IsReady
                    ? localizer.Get(culture, CommandUiKeys.NotesGenshinNotClaimed)
                    : localizer.Get(culture, CommandUiKeys.NotesGenshinClaimed),
                Reset(transformer.ResetAt, localizer, culture), "genshin", "transformer"));
        return new(components, true, true);
    }

    private static CommandPresentation PresentHsrNotes(HsrNotesOutcome notes, ICommandUiLocalizer localizer,
        CultureInfo culture) => new(
    [
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesHsrTitle, notes.GameUid), TextType.Header2),
        Section(localizer.Get(culture, CommandUiKeys.NotesHsrTrailblazePower), Count(notes.TrailblazePower),
            Recovery(notes.TrailblazePower, localizer.Get(culture, CommandUiKeys.NotesGenshinAlreadyFull), localizer,
                culture), "hsr", "tbp"),
        Section(localizer.Get(culture, CommandUiKeys.NotesHsrEchoesOfWar),
            notes.EchoesOfWarRemaining > 0
                ? localizer.Get(culture, CommandUiKeys.NotesHsrClaimed, notes.EchoesOfWarLimit - notes.EchoesOfWarRemaining,
                    notes.EchoesOfWarLimit)
                : localizer.Get(culture, CommandUiKeys.NotesHsrFullyClaimed),
            Reset(notes.EchoesOfWarResetAt, localizer, culture), "hsr", "weekly"),
        Section(localizer.Get(culture, CommandUiKeys.NotesHsrSimulatedUniverse), Count(notes.SimulatedUniverse),
            Reset(notes.SimulatedUniverse.ResetAt, localizer, culture), "hsr", "rogue"),
        Section(localizer.Get(culture, CommandUiKeys.NotesHsrCurrencyWars), Count(notes.CurrencyWars),
            Reset(notes.CurrencyWars.ResetAt, localizer, culture), "hsr", "gridfight")
    ], true, true);

    private static string SeasonCounter(NotesCounter? counter, ICommandUiLocalizer localizer, CultureInfo culture,
        bool newline = true) => counter == null ? "-" :
        Count(counter) + (counter.ResetAt is { } reset
            ? $"{(newline ? "\n" : "")}{localizer.Get(culture, CommandUiKeys.NotesRefreshesIn, Timestamp(reset, 'F'))}"
            : "");

    private static CommandPresentation PresentZzzNotes(ZzzNotesOutcome notes, ICommandUiLocalizer localizer,
        CultureInfo culture) => new(
    [
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzTitle, notes.GameUid), TextType.Header2),
        Section(localizer.Get(culture, CommandUiKeys.NotesZzzBattery), Count(notes.Battery),
            Recovery(notes.Battery, localizer.Get(culture, CommandUiKeys.NotesZzzFullyRecovered), localizer, culture),
            "zzz", "battery"),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzDailyMissions), TextType.Header3),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzDailyEngagement, Count(notes.DailyEngagement))),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzScratchCard, notes.ScratchCard switch
        {
            ScratchCardOutcome.Incomplete => localizer.Get(culture, CommandUiKeys.NotesStateIncomplete),
            ScratchCardOutcome.Completed => localizer.Get(culture, CommandUiKeys.NotesStateCompleted),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        })),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzVideoStore, notes.VideoStore switch
        {
            VideoStoreOutcome.WaitingToOpen => localizer.Get(culture, CommandUiKeys.NotesStateWaitingToOpen),
            VideoStoreOutcome.Open => localizer.Get(culture, CommandUiKeys.NotesStateOpen),
            VideoStoreOutcome.RevenueAvailable => localizer.Get(culture, CommandUiKeys.NotesStateRevenueAvailable),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        })),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzSeasonMissions), TextType.Header3),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzBountyCommission,
            SeasonCounter(notes.BountyCommission, localizer, culture))),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzWeeklyPoints,
            SeasonCounter(notes.WeeklyPoints, localizer, culture))),
        // Preserve the baseline Omnicoins line (unlike the other counters it has no newline).
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzOmnicoins,
            SeasonCounter(notes.Omnicoins, localizer, culture, false))),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzSuibianTemple), TextType.Header3),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzAdventure, notes.Adventure switch
        {
            TempleAdventureOutcome.Dispatchable => localizer.Get(culture, CommandUiKeys.NotesStateDispatchable),
            TempleAdventureOutcome.Adventuring => localizer.Get(culture, CommandUiKeys.NotesStateAdventuring),
            TempleAdventureOutcome.Completed => localizer.Get(culture, CommandUiKeys.NotesStateCompleted),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        })),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzCraftingWorkshop, notes.Crafting switch
        {
            TempleCraftingOutcome.Crafting => localizer.Get(culture, CommandUiKeys.NotesStateCrafting),
            TempleCraftingOutcome.Craftable => localizer.Get(culture, CommandUiKeys.NotesStateCraftable),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        })),
        new CommandText(localizer.Get(culture, CommandUiKeys.NotesZzzSalesStall, notes.Sales switch
        {
            TempleSalesOutcome.NotSelling => localizer.Get(culture, CommandUiKeys.NotesStateNotSelling),
            TempleSalesOutcome.Selling => localizer.Get(culture, CommandUiKeys.NotesStateSelling),
            TempleSalesOutcome.OutOfStock => localizer.Get(culture, CommandUiKeys.NotesStateOutOfStock),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        }))
    ], true, true);
}
