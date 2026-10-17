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
    private static string Reset(DateTimeOffset? time) => time is { } reset ? $"Resets {Timestamp(reset, 'R')}" : "-";
    private static string Recovery(NotesResource resource, string fullText) =>
        resource.RecoveryAt is { } time ? $"Recovers {Timestamp(time, 'R')}" : fullText;

    private static CommandPresentation PresentGenshinNotes(GenshinNotesOutcome notes)
    {
        var expeditionValue = notes.Expeditions.State == ExpeditionOutcome.Undispatched
            ? "None Dispatched!" : $"{notes.Expeditions.Current}/{notes.Expeditions.Maximum}";
        var expeditionFooter = notes.Expeditions.State switch
        {
            ExpeditionOutcome.Undispatched => "To be dispatched",
            ExpeditionOutcome.Completed => "All Expeditions Completed",
            ExpeditionOutcome.InProgress when notes.Expeditions.CompletionAt is { } time => $"Completes {Timestamp(time, 'R')}",
            _ => "Unknown"
        };
        List<IPresentationComponent> components =
        [
            new CommandText($"Real Time Notes for UID: {notes.GameUid}", TextType.Header2),
            Section("Original Resin", Count(notes.Resin), Recovery(notes.Resin, "Already Full!"), "genshin", "resin"),
            Section("Expeditions", expeditionValue, expeditionFooter, "genshin", "expedition"),
            Section("Serenitea Pot", notes.HomeCoin.RecoveryAt == null ? "Already Full!" : Count(notes.HomeCoin),
                Recovery(notes.HomeCoin, "To be collected"), "genshin", "teapot"),
            Section("Weekly Bosses", $"Remaining Resin Discount: {notes.RemainingWeeklyDiscounts}/{notes.WeeklyDiscountLimit}",
                Reset(notes.WeeklyResetAt), "genshin", "weekly")
        ];
        if (notes.Transformer is { } transformer)
            components.Add(Section("Parametric Transformer", transformer.IsReady ? "Not Claimed!" : "Claimed!",
                Reset(transformer.ResetAt), "genshin", "transformer"));
        return new(components, true, true);
    }

    private static CommandPresentation PresentHsrNotes(HsrNotesOutcome notes) => new(
    [
        new CommandText($"Honkai Star Rail Real-Time Notes (UID: {notes.GameUid})", TextType.Header2),
        Section("Trailblaze Power", Count(notes.TrailblazePower), Recovery(notes.TrailblazePower, "Already Full!"), "hsr", "tbp"),
        Section("Echoes of War", notes.EchoesOfWarRemaining > 0
            ? $"Claimed {notes.EchoesOfWarLimit - notes.EchoesOfWarRemaining}/{notes.EchoesOfWarLimit}" : "Fully Claimed!",
            Reset(notes.EchoesOfWarResetAt), "hsr", "weekly"),
        Section("Simulated Universe", Count(notes.SimulatedUniverse), Reset(notes.SimulatedUniverse.ResetAt), "hsr", "rogue"),
        Section("Currency Wars", Count(notes.CurrencyWars), Reset(notes.CurrencyWars.ResetAt), "hsr", "gridfight")
    ], true, true);

    private static string SeasonCounter(NotesCounter? counter, bool newline = true) => counter == null ? "-" :
        Count(counter) + (counter.ResetAt is { } reset ? $"{(newline ? "\n" : "")}-# Refreshes in {Timestamp(reset, 'F')}" : "");

    private static CommandPresentation PresentZzzNotes(ZzzNotesOutcome notes) => new(
    [
        new CommandText($"Zenless Zone Zero Real-Time Notes (UID: {notes.GameUid})", TextType.Header2),
        Section("Battery Charge", Count(notes.Battery), Recovery(notes.Battery, "Fully Recovered!"), "zzz", "battery"),
        new CommandText("Daily Missions", TextType.Header3),
        new CommandText($"Daily Engagement: {Count(notes.DailyEngagement)}"),
        new CommandText($"Scratch Card/Divination: {notes.ScratchCard switch
        {
            ScratchCardOutcome.Incomplete => "Incomplete",
            ScratchCardOutcome.Completed => "Completed",
            _ => "Unknown"
        }}"),
        new CommandText($"Video Store Management: {notes.VideoStore switch
        {
            VideoStoreOutcome.WaitingToOpen => "Waiting to Open",
            VideoStoreOutcome.Open => "Currently Open",
            VideoStoreOutcome.RevenueAvailable => "Revenue Available",
            _ => "Unknown"
        }}"),
        new CommandText("Season Missions", TextType.Header3),
        new CommandText($"Bounty Commission: {SeasonCounter(notes.BountyCommission)}"),
        new CommandText($"Ridu Weekly Points: {SeasonCounter(notes.WeeklyPoints)}"),
        // Preserve the baseline Omnicoins line (unlike the other counters it has no newline).
        new CommandText($"Omnicoins: {SeasonCounter(notes.Omnicoins, false)}"),
        new CommandText("Suibian Temple Management", TextType.Header3),
        new CommandText($"Adventure: {notes.Adventure switch
        {
            TempleAdventureOutcome.Dispatchable => "Dispatchable",
            TempleAdventureOutcome.Adventuring => "Adventuring",
            TempleAdventureOutcome.Completed => "Completed",
            _ => "Unknown"
        }}"),
        new CommandText($"Crafting Workshop: {notes.Crafting switch
        {
            TempleCraftingOutcome.Crafting => "Crafting",
            TempleCraftingOutcome.Craftable => "Craftable",
            _ => "Unknown"
        }}"),
        new CommandText($"Sales Stall: {notes.Sales switch
        {
            TempleSalesOutcome.NotSelling => "Not Selling",
            TempleSalesOutcome.Selling => "Selling",
            TempleSalesOutcome.OutOfStock => "Out of Stock",
            _ => "Unknown"
        }}")
    ], true, true);
}
