using System.Globalization;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Models;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using static Mehrak.Bot.Shared.Models.CommandText;

namespace Mehrak.Bot.Shared.Services;

internal static partial class CommandOutcomePresenter
{
    internal const string ApiLimitationFooter =
        "Information may be inaccurate due to API limitations. Please check in-game for the most accurate data.";

    /// <summary>Neutral English presentation used by tests and legacy callers.</summary>
    public static CommandPresentation Present(CommandOutcome? outcome, ulong userId) =>
        Present(outcome, userId, CommandUiLocalizer.Default, BotUiCulture.English);

    /// <summary>Culture-explicit presentation. The culture is resolved per interaction by the caller.</summary>
    public static CommandPresentation Present(CommandOutcome? outcome, ulong userId,
        ICommandUiLocalizer localizer, CultureInfo culture) => outcome switch
        {
            CharacterCardsOutcome characters => PresentCharacters(characters, userId, localizer, culture),
            RosterOutcome roster => roster.Image is { } image
                ? new([new CommandText($"<@{userId}>"), Attachment(image)], false, false)
                : PrivateText(localizer.Get(culture, CommandUiKeys.PresenterNoCharacters)),
            EndgameOutcome endgame => PresentEndgame(endgame, userId, localizer, culture),
            GenshinNotesOutcome notes => PresentGenshinNotes(notes, localizer, culture),
            HsrNotesOutcome notes => PresentHsrNotes(notes, localizer, culture),
            ZzzNotesOutcome notes => PresentZzzNotes(notes, localizer, culture),
            CodeRedemptionOutcome codes => PresentCodes(codes, localizer, culture),
            DailyCheckInOutcome checkIn => PresentCheckIn(checkIn, localizer, culture),
            _ => PrivateText(ErrorText(null, localizer, culture))
        };

    // Envelope failure takes precedence even if it retains previously generated items.
    /// <summary>Neutral English safe error text.</summary>
    public static string SafeErrorText(CommandError? error) =>
        ErrorText(error, CommandUiLocalizer.Default, BotUiCulture.English);

    /// <summary>
    /// Maps the error code to a safe translated category. Exception text and upstream messages are
    /// never surfaced; the Domain <see cref="CommandError.Message"/> remains the neutral fallback.
    /// </summary>
    public static string ErrorText(CommandError? error, ICommandUiLocalizer localizer, CultureInfo culture)
    {
        var key = (error?.Code ?? CommandErrorCode.InvalidResponse) switch
        {
            CommandErrorCode.AuthenticationRequired => CommandUiKeys.ErrorAuthenticationRequired,
            CommandErrorCode.ApiUnavailable => CommandUiKeys.ErrorApiUnavailable,
            CommandErrorCode.ImageUpdateFailed => CommandUiKeys.ErrorImageUpdateFailed,
            CommandErrorCode.AttachmentStoreFailed => CommandUiKeys.ErrorAttachmentStoreFailed,
            CommandErrorCode.Cancelled => CommandUiKeys.ErrorCancelled,
            CommandErrorCode.Timeout => CommandUiKeys.ErrorTimeout,
            CommandErrorCode.InvalidRequest => CommandUiKeys.ErrorInvalidRequest,
            CommandErrorCode.InvalidResponse => CommandUiKeys.ErrorInvalidResponse,
            CommandErrorCode.CharacterNotFound => CommandUiKeys.ErrorCharacterNotFound,
            CommandErrorCode.RequestLimitExceeded => CommandUiKeys.ErrorRequestLimitExceeded,
            _ => CommandUiKeys.ErrorUnexpected
        };

        return localizer.Get(culture, key);
    }

    private static CommandPresentation PrivateText(string text) => new([new CommandText(text)], false, true);

    private static CommandAttachment Attachment(StorageReference image) =>
        new(image.StorageFileName, image.SourceType, image.StorageFileName);

    private static CommandPresentation PresentCharacters(CharacterCardsOutcome outcome, ulong userId,
        ICommandUiLocalizer localizer, CultureInfo culture)
    {
        if (outcome.State == OperationOutcome.RequestLimitExceeded)
            return PrivateText(localizer.Get(culture, CommandUiKeys.PresenterCharacterLimit,
                outcome.RequestLimit?.ToString() ?? string.Empty));

        var errors = outcome.Items.Where(x => x.Status != CharacterCardStatus.Generated)
            .Select(x => x.Status switch
            {
                CharacterCardStatus.NotFound => localizer.Get(culture, CommandUiKeys.PresenterCharacterNotFound,
                    x.RequestedName),
                CharacterCardStatus.NotAttempted => localizer.Get(culture, CommandUiKeys.PresenterNotAttemptedItem,
                    x.CharacterName ?? x.RequestedName),
                _ => localizer.Get(culture, CommandUiKeys.PresenterItemLine, x.CharacterName ?? x.RequestedName,
                    ErrorText(x.Error, localizer, culture))
            }).ToArray();
        var images = outcome.Items.Where(x => x.Status == CharacterCardStatus.Generated && x.Image != null).ToArray();
        if (images.Length == 0)
            return PrivateText(errors.Length > 0
                ? string.Join('\n', errors)
                : localizer.Get(culture, CommandUiKeys.PresenterNoCharacters));

        var mentionStyle = outcome.Game is Game.ZenlessZoneZero or Game.HonkaiImpact3 ? TextType.Header3 : TextType.Plain;
        List<IPresentationComponent> components = [new CommandText($"<@{userId}>", mentionStyle)];
        components.AddRange(images.Select(x => Attachment(x.Image!)));
        return new(components, false, false, errors.Length > 0 ? string.Join('\n', errors) : null);
    }

    private static CommandPresentation PresentEndgame(EndgameOutcome outcome, ulong userId,
        ICommandUiLocalizer localizer, CultureInfo culture)
    {
        var mode = outcome.Mode switch
        {
            EndgameMode.SpiralAbyss => localizer.Get(culture, CommandUiKeys.PresenterEndgameSpiralAbyss),
            EndgameMode.ImaginariumTheater => localizer.Get(culture, CommandUiKeys.PresenterEndgameImaginariumTheater),
            EndgameMode.StygianOnslaught => localizer.Get(culture, CommandUiKeys.PresenterEndgameStygianOnslaught),
            EndgameMode.MemoryOfChaos => localizer.Get(culture, CommandUiKeys.PresenterEndgameMemoryOfChaos),
            EndgameMode.PureFiction => localizer.Get(culture, CommandUiKeys.PresenterEndgamePureFiction),
            EndgameMode.ApocalypticShadow => localizer.Get(culture, CommandUiKeys.PresenterEndgameApocalypticShadow),
            EndgameMode.AnomalyArbitration => localizer.Get(culture, CommandUiKeys.PresenterEndgameAnomalyArbitration),
            EndgameMode.ShiyuDefense => localizer.Get(culture, CommandUiKeys.PresenterEndgameShiyuDefense),
            EndgameMode.DeadlyAssault => localizer.Get(culture, CommandUiKeys.PresenterEndgameDeadlyAssault),
            EndgameMode.SimulatedBattleTrial => localizer.Get(culture, CommandUiKeys.PresenterEndgameSimulatedBattleTrial),
            _ => localizer.Get(culture, CommandUiKeys.NotesStateUnknown)
        };
        var floor = outcome.Mode == EndgameMode.SpiralAbyss
            ? localizer.Get(culture, CommandUiKeys.PresenterEndgameFloor, outcome.Floor?.ToString() ?? string.Empty)
            : "";
        if (outcome.State == OperationOutcome.NotUnlocked)
            // The existing Theater locked response is intentionally public; Stygian is private.
            return new([new CommandText(localizer.Get(culture, CommandUiKeys.PresenterEndgameLocked, mode))], false,
                outcome.Mode != EndgameMode.ImaginariumTheater);
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText(localizer.Get(culture, CommandUiKeys.PresenterEndgameEmpty, mode, floor));
        if (outcome.Image == null)
            return PrivateText(ErrorText(null, localizer, culture));

        List<IPresentationComponent> components =
        [
            new CommandText(localizer.Get(culture, CommandUiKeys.PresenterEndgameTitle, $"<@{userId}>", mode, floor),
                TextType.Header3)
        ];
        if (outcome.Mode != EndgameMode.SimulatedBattleTrial)
        {
            List<string> cycle = [];
            if (outcome.CycleStart is { } start)
                cycle.Add(localizer.Get(culture, CommandUiKeys.PresenterEndgameCycleStart, Timestamp(start, 'f')));
            if (outcome.CycleEnd is { } end)
                cycle.Add(localizer.Get(culture, CommandUiKeys.PresenterEndgameCycleEnd, Timestamp(end, 'f')));
            if (cycle.Count > 0) components.Add(new CommandText(string.Join('\n', cycle)));
        }
        components.Add(Attachment(outcome.Image));
        components.Add(new CommandText(localizer.Get(culture, CommandUiKeys.PresenterApiLimitationFooter), TextType.Footer));
        // Tower's private override remains in its Bot command builder, not on the wire.
        return new(components, true, false);
    }

    private static CommandPresentation PresentCodes(CodeRedemptionOutcome outcome, ICommandUiLocalizer localizer,
        CultureInfo culture)
    {
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText(localizer.Get(culture, CommandUiKeys.PresenterNoCodes));
        return PrivateText(string.Join('\n', outcome.Items.Select(x => localizer.Get(culture,
            CommandUiKeys.PresenterItemLine, x.Code, x.Outcome switch
            {
                RedemptionOutcome.Redeemed => localizer.Get(culture, CommandUiKeys.PresenterRedeemed),
                RedemptionOutcome.Expired => localizer.Get(culture, CommandUiKeys.PresenterCodeExpired),
                RedemptionOutcome.Invalid => localizer.Get(culture, CommandUiKeys.PresenterCodeInvalid),
                RedemptionOutcome.Cooldown => localizer.Get(culture, CommandUiKeys.PresenterCodeCooldown),
                RedemptionOutcome.AlreadyUsed => localizer.Get(culture, CommandUiKeys.PresenterCodeAlreadyUsed),
                RedemptionOutcome.NotAttempted => localizer.Get(culture, CommandUiKeys.PresenterNotAttempted),
                _ => localizer.Get(culture, CommandUiKeys.PresenterCodeError)
            }))));
    }

    private static CommandPresentation PresentCheckIn(DailyCheckInOutcome outcome, ICommandUiLocalizer localizer,
        CultureInfo culture)
    {
        if (outcome.State == OperationOutcome.AlreadyCompleted)
            return PrivateText(localizer.Get(culture, CommandUiKeys.PresenterCheckInAlreadyDone));
        if (outcome.State == OperationOutcome.Empty)
            return PrivateText(localizer.Get(culture, CommandUiKeys.PresenterNoGameRecords));
        return PrivateText(string.Join('\n', outcome.Items.Select(x => localizer.Get(culture,
            CommandUiKeys.PresenterItemLine, x.Game.ToFriendlyString(), x.Outcome switch
            {
                CheckInOutcome.Succeeded => localizer.Get(culture, CommandUiKeys.PresenterCheckInSuccess),
                CheckInOutcome.AlreadyCheckedIn => localizer.Get(culture, CommandUiKeys.PresenterAlreadyCheckedIn),
                CheckInOutcome.NoValidProfile => localizer.Get(culture, CommandUiKeys.PresenterNoValidAccount),
                CheckInOutcome.NotAttempted => localizer.Get(culture, CommandUiKeys.PresenterNotAttempted),
                CheckInOutcome.Failed when x.Error?.Code == CommandErrorCode.Timeout =>
                    localizer.Get(culture, CommandUiKeys.PresenterCheckInTimeout),
                CheckInOutcome.Failed => ErrorText(x.Error, localizer, culture),
                _ => localizer.Get(culture, CommandUiKeys.PresenterUnknownStatus)
            }))));
    }

    private static string Timestamp(DateTimeOffset time, char style) => $"<t:{time.ToUnixTimeSeconds()}:{style}>";
}
