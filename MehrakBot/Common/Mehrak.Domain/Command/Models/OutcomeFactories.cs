using System.Collections.Immutable;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Command.Models;

public sealed partial record StorageReference
{
    public static StorageReference Attachment(string storageFileName) => new(AttachmentSourceType.AttachmentStorage, storageFileName);
    public static StorageReference Image(string storageFileName) => new(AttachmentSourceType.ImageStorage, storageFileName);
}

public sealed partial record CommandError
{
    // Safe by construction: no caller-supplied exception or upstream text is exposed.
    public string Message => Code switch
    {
        CommandErrorCode.AuthenticationRequired => "Invalid HoYoLAB UID or cookies. Please re-authenticate.",
        CommandErrorCode.ApiUnavailable => "Unable to retrieve game data. Please try again later.",
        CommandErrorCode.ImageUpdateFailed => "Unable to update the required images. Please try again later.",
        CommandErrorCode.AttachmentStoreFailed => "Unable to store the generated image. Please try again later.",
        CommandErrorCode.Cancelled => "Command execution cancelled.",
        CommandErrorCode.Timeout => "Command execution timed out. Please try again later.",
        CommandErrorCode.InvalidRequest => "Invalid command request.",
        CommandErrorCode.InvalidResponse => "The command returned an unsupported or invalid response.",
        CommandErrorCode.CharacterNotFound => "Character not found.",
        CommandErrorCode.RequestLimitExceeded => "Exceeded the maximum number of characters per request.",
        _ => "An unexpected error occurred. Please try again later."
    };
}

public sealed partial record CharacterCardItem
{
    public static CharacterCardItem Generated(string requestedName, string characterName, int characterId, StorageReference image) =>
        new(requestedName, characterName, characterId, CharacterCardStatus.Generated, image, null);
    public static CharacterCardItem NotFound(string requestedName) =>
        new(requestedName, null, null, CharacterCardStatus.NotFound, null,
            new(CommandErrorCode.CharacterNotFound, CommandErrorTarget.CharacterData));
    public static CharacterCardItem Failed(string requestedName, CommandError error, string? characterName = null, int? characterId = null) =>
        new(requestedName, characterName, characterId, CharacterCardStatus.Failed, null, error);
    public static CharacterCardItem NotAttempted(string requestedName, string? characterName = null, int? characterId = null) =>
        new(requestedName, characterName, characterId, CharacterCardStatus.NotAttempted, null, null);
}

public sealed partial record CharacterCardsOutcome
{
    public static CharacterCardsOutcome Create(Game game, string? gameUid, IEnumerable<CharacterCardItem> items,
        OperationOutcome state = OperationOutcome.Complete, int? requestLimit = null) =>
        new(game, gameUid, state, items.ToImmutableArray(), requestLimit);
    public static CharacterCardsOutcome LimitExceeded(Game game, IEnumerable<string> requestedNames, int limit) =>
        Create(game, null, requestedNames.Select(x => CharacterCardItem.NotAttempted(x)), OperationOutcome.RequestLimitExceeded, limit);
}

public sealed partial record RosterOutcome
{
    public static RosterOutcome Card(Game game, string gameUid, StorageReference image) => new(game, gameUid, OperationOutcome.Complete, image);
    public static RosterOutcome Empty(Game game, string gameUid) => new(game, gameUid, OperationOutcome.Empty, null);
}

public sealed partial record EndgameOutcome
{
    public static EndgameOutcome Card(Game game, string gameUid, EndgameMode mode, StorageReference image,
        DateTimeOffset? cycleStart = null, DateTimeOffset? cycleEnd = null, int? floor = null) =>
        new(game, gameUid, mode, OperationOutcome.Complete, image, floor, cycleStart, cycleEnd);
    public static EndgameOutcome Empty(Game game, string gameUid, EndgameMode mode, int? floor = null) =>
        new(game, gameUid, mode, OperationOutcome.Empty, null, floor, null, null);
    public static EndgameOutcome Locked(Game game, string gameUid, EndgameMode mode) =>
        new(game, gameUid, mode, OperationOutcome.NotUnlocked, null, null, null, null);
}

public sealed partial record RedemptionItem
{
    public static RedemptionItem Completed(string code, RedemptionOutcome outcome) => new(code, outcome, null);
    public static RedemptionItem Failed(string code, CommandError error) => new(code, RedemptionOutcome.Failed, error);
    public static RedemptionItem NotAttempted(string code) => new(code, RedemptionOutcome.NotAttempted, null);
}

public sealed partial record CodeRedemptionOutcome
{
    public static CodeRedemptionOutcome Create(Game game, string gameUid, IEnumerable<RedemptionItem> items,
        OperationOutcome state = OperationOutcome.Complete) => new(game, gameUid, state, items.ToImmutableArray());
    public static CodeRedemptionOutcome Empty(Game game, string gameUid) => new(game, gameUid, OperationOutcome.Empty, []);
}

public sealed partial record CheckInItem
{
    public static CheckInItem Completed(Game game, CheckInOutcome outcome) => new(game, outcome, null);
    public static CheckInItem Failed(Game game, CommandError error) => new(game, CheckInOutcome.Failed, error);
    public static CheckInItem NotAttempted(Game game) => new(game, CheckInOutcome.NotAttempted, null);
}

public sealed partial record DailyCheckInOutcome
{
    public static DailyCheckInOutcome Create(IEnumerable<CheckInItem> items, OperationOutcome state = OperationOutcome.Complete) =>
        new(state, items.ToImmutableArray());
    public static DailyCheckInOutcome Empty() => new(OperationOutcome.Empty, []);
    public static DailyCheckInOutcome AlreadyCompleted() => new(OperationOutcome.AlreadyCompleted, []);
}
