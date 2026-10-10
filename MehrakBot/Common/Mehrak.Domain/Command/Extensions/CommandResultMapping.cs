using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Domain.Command.Extensions;

public static partial class ProtobufMappingExtensions
{
    public static Proto.CommandResult ToProto(this CommandResult domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Validate(domain.Outcome);
        var proto = new Proto.CommandResult
        {
            IsSuccess = domain.IsSuccess,
            FailureReason = (Proto.CommandFailureReason)Checked(domain.FailureReason),
            Error = domain.Error?.ToProto()
        };
        switch (domain.Outcome)
        {
            case CharacterCardsOutcome outcome: proto.CharacterCards = outcome.ToProto(); break;
            case RosterOutcome outcome: proto.Roster = outcome.ToProto(); break;
            case EndgameOutcome outcome: proto.Endgame = outcome.ToProto(); break;
            case GenshinNotesOutcome outcome: proto.GenshinNotes = outcome.ToProto(); break;
            case HsrNotesOutcome outcome: proto.HsrNotes = outcome.ToProto(); break;
            case ZzzNotesOutcome outcome: proto.ZzzNotes = outcome.ToProto(); break;
            case CodeRedemptionOutcome outcome: proto.CodeRedemption = outcome.ToProto(); break;
            case DailyCheckInOutcome outcome: proto.DailyCheckIn = outcome.ToProto(); break;
            case null: break;
            default: throw new ArgumentException("Unsupported command outcome.", nameof(domain));
        }
        return proto;
    }

    /// <summary>Rejects unknown, missing or malformed wire data without exposing wire error text.</summary>
    public static CommandResult ToDomain(this Proto.CommandResult proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        try
        {
            var reason = Checked((CommandFailureReason)proto.FailureReason);
            CommandOutcome? outcome = proto.OutcomeCase switch
            {
                Proto.CommandResult.OutcomeOneofCase.CharacterCards => proto.CharacterCards.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.Roster => proto.Roster.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.Endgame => proto.Endgame.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.GenshinNotes => proto.GenshinNotes.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.HsrNotes => proto.HsrNotes.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.ZzzNotes => proto.ZzzNotes.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.CodeRedemption => proto.CodeRedemption.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.DailyCheckIn => proto.DailyCheckIn.ToDomain(),
                Proto.CommandResult.OutcomeOneofCase.None => null,
                _ => throw new ArgumentException("Unknown command outcome.")
            };
            Validate(outcome);
            if (proto.IsSuccess)
            {
                if (outcome is null || proto.Error is not null || reason != CommandFailureReason.Unknown)
                    return InvalidResponse();
                return CommandResult.Success(outcome);
            }
            if (proto.Error is null) return InvalidResponse();
            return CommandResult.Failure(reason, proto.Error.ToDomain(), outcome);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            return InvalidResponse();
        }
    }

    private static CommandResult InvalidResponse() =>
        CommandResult.Failure(CommandFailureReason.BotError, CommandErrorCode.InvalidResponse);

    private static T Required<T>(T? value) where T : class => value ?? throw new ArgumentException("Missing required response field.");

    private static T Checked<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentException("Unknown response enum value.");

    private static Proto.CommandGame ToCommandGame(this Game game) => game switch
    {
        Game.Unsupported => Proto.CommandGame.Unknown,
        Game.Genshin => Proto.CommandGame.Genshin,
        Game.HonkaiStarRail => Proto.CommandGame.HonkaiStarRail,
        Game.ZenlessZoneZero => Proto.CommandGame.ZenlessZoneZero,
        Game.HonkaiImpact3 => Proto.CommandGame.HonkaiImpact3,
        Game.TearsOfThemis => Proto.CommandGame.TearsOfThemis,
        _ => throw new ArgumentException("Unknown game.", nameof(game))
    };

    private static Game ToGame(this Proto.CommandGame game) => game switch
    {
        Proto.CommandGame.Unknown => Game.Unsupported,
        Proto.CommandGame.Genshin => Game.Genshin,
        Proto.CommandGame.HonkaiStarRail => Game.HonkaiStarRail,
        Proto.CommandGame.ZenlessZoneZero => Game.ZenlessZoneZero,
        Proto.CommandGame.HonkaiImpact3 => Game.HonkaiImpact3,
        Proto.CommandGame.TearsOfThemis => Game.TearsOfThemis,
        _ => throw new ArgumentException("Unknown game.", nameof(game))
    };

    // Preserve request serialization, including parameter string conversion, unchanged.
    public static Proto.ExecuteRequest ToExecuteRequest(string commandName, ulong userId, ulong ltUid, string lToken,
        IEnumerable<(string Key, object Value)> commandParams)
    {
        var request = new Proto.ExecuteRequest
        {
            CommandName = commandName,
            DiscordUserId = userId,
            LtUid = ltUid,
            LToken = lToken ?? string.Empty
        };
        foreach (var (key, value) in commandParams)
            request.Parameters.Add(key, value?.ToString() ?? string.Empty);
        return request;
    }

    private static void Validate(CommandOutcome? outcome)
    {
        static void State(OperationOutcome state)
        {
            Checked(state);
            if (state == OperationOutcome.Unknown) throw new ArgumentException("Missing operation state.");
        }
        static void Image(StorageReference? image)
        {
            if (image is not null && (image.SourceType == AttachmentSourceType.Unknown ||
                !Enum.IsDefined(image.SourceType) || string.IsNullOrWhiteSpace(image.StorageFileName)))
                throw new ArgumentException("Invalid storage reference.");
        }
        switch (outcome)
        {
            case CharacterCardsOutcome cards:
                State(cards.State);
                if (cards.Items.IsDefault) throw new ArgumentException("Missing items.");
                if (cards.State == OperationOutcome.RequestLimitExceeded && cards.RequestLimit is not > 0)
                    throw new ArgumentException("Missing request limit.");
                foreach (var item in cards.Items)
                {
                    if (item is null || item.Status == CharacterCardStatus.Unknown) throw new ArgumentException("Missing character item status.");
                    Image(item.Image);
                    if (item.Status == CharacterCardStatus.Generated && item.Image is null) throw new ArgumentException("Missing character image.");
                    if (item.Status != CharacterCardStatus.Generated && item.Image is not null) throw new ArgumentException("Unexpected character image.");
                    if (item.Status == CharacterCardStatus.Failed && item.Error is null) throw new ArgumentException("Missing character error.");
                    if (item.Status is CharacterCardStatus.Generated or CharacterCardStatus.NotAttempted && item.Error is not null)
                        throw new ArgumentException("Unexpected character error.");
                }
                break;
            case RosterOutcome roster:
                State(roster.State);
                Image(roster.Image);
                if (roster.State == OperationOutcome.Complete && roster.Image is null) throw new ArgumentException("Missing roster image.");
                if (roster.State == OperationOutcome.Empty && roster.Image is not null) throw new ArgumentException("Unexpected roster image.");
                break;
            case EndgameOutcome endgame:
                State(endgame.State);
                if (endgame.Mode == EndgameMode.Unknown) throw new ArgumentException("Missing endgame mode.");
                Image(endgame.Image);
                if (endgame.State == OperationOutcome.Complete && endgame.Image is null) throw new ArgumentException("Missing endgame image.");
                if (endgame.State is OperationOutcome.Empty or OperationOutcome.NotUnlocked && endgame.Image is not null)
                    throw new ArgumentException("Unexpected endgame image.");
                break;
            case CodeRedemptionOutcome redemption:
                State(redemption.State);
                if (redemption.Items.IsDefault) throw new ArgumentException("Missing redemption items.");
                foreach (var item in redemption.Items)
                    if (item is null || item.Outcome == RedemptionOutcome.Unknown ||
                        ((item.Outcome == RedemptionOutcome.Failed) != (item.Error is not null)))
                        throw new ArgumentException("Invalid redemption item.");
                break;
            case DailyCheckInOutcome checkIn:
                State(checkIn.State);
                if (checkIn.Items.IsDefault) throw new ArgumentException("Missing check-in items.");
                foreach (var item in checkIn.Items)
                    if (item is null || ((item.Outcome == CheckInOutcome.Failed) != (item.Error is not null)))
                        throw new ArgumentException("Invalid check-in item.");
                break;
        }
    }
}
