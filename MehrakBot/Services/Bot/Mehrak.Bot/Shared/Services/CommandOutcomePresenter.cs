using Mehrak.Bot.Shared.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using static Mehrak.Bot.Shared.Models.CommandText;

namespace Mehrak.Bot.Shared.Services;

internal static partial class CommandOutcomePresenter
{
    internal const string ApiLimitationFooter =
        "Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.";

    public static CommandPresentation Present(CommandOutcome? outcome, ulong userId) => outcome switch
    {
        CharacterCardsOutcome characters => PresentCharacters(characters, userId),
        RosterOutcome roster => roster.Image is { } image
            ? new([new CommandText($"<@{userId}>"), Attachment(image)], false, false)
            : PrivateText("No characters found."),
        EndgameOutcome endgame => PresentEndgame(endgame, userId),
        GenshinNotesOutcome notes => PresentGenshinNotes(notes),
        HsrNotesOutcome notes => PresentHsrNotes(notes),
        ZzzNotesOutcome notes => PresentZzzNotes(notes),
        CodeRedemptionOutcome codes => PresentCodes(codes),
        DailyCheckInOutcome checkIn => PresentCheckIn(checkIn),
        _ => PrivateText(new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None).Message)
    };

    // Envelope failure takes precedence even if it retains previously generated items.
    public static string SafeErrorText(CommandError? error) =>
        (error ?? new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None)).Message;

    private static CommandPresentation PrivateText(string text) => new([new CommandText(text)], false, true);

    private static CommandAttachment Attachment(StorageReference image) =>
        new(image.StorageFileName, image.SourceType, image.StorageFileName);

    private static CommandPresentation PresentCharacters(CharacterCardsOutcome outcome, ulong userId)
    {
        if (outcome.State == OperationOutcome.RequestLimitExceeded)
            return PrivateText($"Exceeded the maximum number of characters per request! (Max {outcome.RequestLimit})");

        var errors = outcome.Items.Where(x => x.Status != CharacterCardStatus.Generated)
            .Select(x => x.Status switch
            {
                CharacterCardStatus.NotFound => $"Character {x.RequestedName} not found. Please check the name and try again",
                CharacterCardStatus.NotAttempted => $"{x.CharacterName ?? x.RequestedName}: Not attempted.",
                _ => $"{x.CharacterName ?? x.RequestedName}: {SafeErrorText(x.Error)}"
            }).ToArray();
        var images = outcome.Items.Where(x => x.Status == CharacterCardStatus.Generated && x.Image != null).ToArray();
        if (images.Length == 0)
            return PrivateText(errors.Length > 0 ? string.Join('\n', errors) : "No characters found.");

        var mentionStyle = outcome.Game is Game.ZenlessZoneZero or Game.HonkaiImpact3 ? TextType.Header3 : TextType.Plain;
        List<IPresentationComponent> components = [new CommandText($"<@{userId}>", mentionStyle)];
        components.AddRange(images.Select(x => Attachment(x.Image!)));
        return new(components, false, false, errors.Length > 0 ? string.Join('\n', errors) : null);
    }

    private static CommandPresentation PresentEndgame(EndgameOutcome outcome, ulong userId)
    {
        var mode = outcome.Mode switch
        {
            EndgameMode.SpiralAbyss => "Spiral Abyss",
            EndgameMode.ImaginariumTheater => "Imaginarium Theater",
            EndgameMode.StygianOnslaught => "Stygian Onslaught",
            EndgameMode.MemoryOfChaos => "Memory of Chaos",
            EndgameMode.PureFiction => "Pure Fiction",
            EndgameMode.ApocalypticShadow => "Apocalyptic Shadow",
            EndgameMode.AnomalyArbitration => "Anomaly Arbitration",
            EndgameMode.ShiyuDefense => "Shiyu Defense",
            EndgameMode.DeadlyAssault => "Deadly Assault",
            EndgameMode.SimulatedBattleTrial => "Simulated Battle Trial",
            _ => "Unknown"
        };
        var floor = outcome.Mode == EndgameMode.SpiralAbyss ? $" (Floor {outcome.Floor})" : "";
        if (outcome.State == OperationOutcome.NotUnlocked)
            // The existing Theater locked response is intentionally public; Stygian is private.
            return new([new CommandText($"{mode} is not unlocked")], false,
                outcome.Mode != EndgameMode.ImaginariumTheater);
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText($"No clear records found for {mode}{floor}");
        if (outcome.Image == null)
            return PrivateText(SafeErrorText(null));

        List<IPresentationComponent> components = [new CommandText($"<@{userId}>'s {mode} Summary{floor}", TextType.Header3)];
        if (outcome.Mode != EndgameMode.SimulatedBattleTrial)
        {
            List<string> cycle = [];
            if (outcome.CycleStart is { } start) cycle.Add($"Cycle start: {Timestamp(start, 'f')}");
            if (outcome.CycleEnd is { } end) cycle.Add($"Cycle end: {Timestamp(end, 'f')}");
            if (cycle.Count > 0) components.Add(new CommandText(string.Join('\n', cycle)));
        }
        components.Add(Attachment(outcome.Image));
        components.Add(new CommandText(ApiLimitationFooter, TextType.Footer));
        // Tower's private override remains in its Bot command builder, not on the wire.
        return new(components, true, false);
    }

    private static CommandPresentation PresentCodes(CodeRedemptionOutcome outcome)
    {
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText("No known codes found in database. Please provide a valid code");
        return PrivateText(string.Join('\n', outcome.Items.Select(x => $"{x.Code}: {x.Outcome switch
        {
            RedemptionOutcome.Redeemed => "Redeemed Successfully!",
            RedemptionOutcome.Expired => "Redemption Code Expired",
            RedemptionOutcome.Invalid => "Invalid Code",
            RedemptionOutcome.Cooldown => "Redemption in Cooldown",
            RedemptionOutcome.AlreadyUsed => "Redemption Code Already Used",
            RedemptionOutcome.NotAttempted => "Not attempted.",
            _ => "An error occurred while redeeming the code"
        }}")));
    }

    private static CommandPresentation PresentCheckIn(DailyCheckInOutcome outcome)
    {
        if (outcome.State == OperationOutcome.AlreadyCompleted)
            return PrivateText("You have already checked in today");
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText("No game records found.");
        return PrivateText(string.Join('\n', outcome.Items.Select(x => $"{x.Game.ToFriendlyString()}: {x.Outcome switch
        {
            CheckInOutcome.Succeeded => "Check-in successful!",
            CheckInOutcome.AlreadyCheckedIn => "Already checked in today.",
            CheckInOutcome.NoValidProfile => "No valid account found.",
            CheckInOutcome.NotAttempted => "Not attempted.",
            CheckInOutcome.Failed when x.Error?.Code == CommandErrorCode.Timeout => "Check-in timed out.",
            CheckInOutcome.Failed => SafeErrorText(x.Error),
            _ => "Unknown status."
        }}")));
    }

    private static string Timestamp(DateTimeOffset time, char style) => $"<t:{time.ToUnixTimeSeconds()}:{style}>";
}
