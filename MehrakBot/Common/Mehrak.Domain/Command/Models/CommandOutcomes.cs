using System.Collections.Immutable;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Command.Models;

public enum CommandFailureReason
{
    Unknown = 0,
    AuthError = 1,
    ApiError = 2,
    BotError = 3,
    Cancelled = 4,
    Timeout = 5,
}

public enum AttachmentSourceType
{
    Unknown = 0,
    ImageStorage = 1,
    AttachmentStorage = 2,
}

public enum OperationOutcome
{
    Unknown = 0,
    Complete = 1,
    Empty = 2,
    NotUnlocked = 3,
    Partial = 4,
    RequestLimitExceeded = 5,
    AlreadyCompleted = 6,
    Failed = 7,
}

public enum CommandErrorCode
{
    Unknown = 0,
    AuthenticationRequired = 1,
    ApiUnavailable = 2,
    ImageUpdateFailed = 3,
    AttachmentStoreFailed = 4,
    Cancelled = 5,
    Timeout = 6,
    InvalidRequest = 7,
    InvalidResponse = 8,
    CharacterNotFound = 9,
    RequestLimitExceeded = 10,
}

public enum CommandErrorTarget
{
    None = 0,
    GameProfile = 1,
    CharacterList = 2,
    CharacterData = 3,
    CharacterImage = 4,
    LightConeData = 5,
    BangbooList = 6,
    RealTimeNotes = 7,
    SpiralAbyss = 8,
    ImaginariumTheater = 9,
    StygianOnslaught = 10,
    MemoryOfChaos = 11,
    PureFiction = 12,
    ApocalypticShadow = 13,
    AnomalyArbitration = 14,
    ShiyuDefense = 15,
    DeadlyAssault = 16,
    SimulatedBattleTrial = 17,
    CodeRedemption = 18,
    DailyCheckIn = 19,
    Image = 20,
    Attachment = 21,
}

public enum CharacterCardStatus
{
    Unknown = 0,
    Generated = 1,
    NotFound = 2,
    Failed = 3,
    NotAttempted = 4,
}

public enum EndgameMode
{
    Unknown = 0,
    SpiralAbyss = 1,
    ImaginariumTheater = 2,
    StygianOnslaught = 3,
    MemoryOfChaos = 4,
    PureFiction = 5,
    ApocalypticShadow = 6,
    AnomalyArbitration = 7,
    ShiyuDefense = 8,
    DeadlyAssault = 9,
    SimulatedBattleTrial = 10,
}

public enum RedemptionOutcome
{
    Unknown = 0,
    Redeemed = 1,
    Expired = 2,
    Invalid = 3,
    Cooldown = 4,
    AlreadyUsed = 5,
    Failed = 6,
    NotAttempted = 7,
}

public enum CheckInOutcome
{
    Unknown = 0,
    Succeeded = 1,
    AlreadyCheckedIn = 2,
    NoValidProfile = 3,
    Failed = 4,
    NotAttempted = 5,
}

public enum ExpeditionOutcome
{
    Unknown = 0,
    Undispatched = 1,
    InProgress = 2,
    Completed = 3,
}

public enum ScratchCardOutcome
{
    Unknown = 0,
    Incomplete = 1,
    Completed = 2,
}

public enum VideoStoreOutcome
{
    Unknown = 0,
    WaitingToOpen = 1,
    Open = 2,
    RevenueAvailable = 3,
}

public enum TempleAdventureOutcome
{
    Unknown = 0,
    Dispatchable = 1,
    Adventuring = 2,
    Completed = 3,
}

public enum TempleCraftingOutcome
{
    Unknown = 0,
    Crafting = 1,
    Craftable = 2,
}

public enum TempleSalesOutcome
{
    Unknown = 0,
    NotSelling = 1,
    Selling = 2,
    OutOfStock = 3,
}

public abstract record CommandOutcome;

public sealed partial record StorageReference(AttachmentSourceType SourceType, string StorageFileName)
{
}

public sealed partial record CommandError(CommandErrorCode Code, CommandErrorTarget Target)
{
}

public sealed partial record CharacterCardItem(string RequestedName, string? CharacterName, int? CharacterId, CharacterCardStatus Status, StorageReference? Image, CommandError? Error)
{
}

public sealed partial record CharacterCardsOutcome(Game Game, string? GameUid, OperationOutcome State, ImmutableArray<CharacterCardItem> Items, int? RequestLimit) : CommandOutcome
{
}

public sealed partial record RosterOutcome(Game Game, string GameUid, OperationOutcome State, StorageReference? Image) : CommandOutcome
{
}

public sealed partial record EndgameOutcome(Game Game, string GameUid, EndgameMode Mode, OperationOutcome State, StorageReference? Image, int? Floor, DateTimeOffset? CycleStart, DateTimeOffset? CycleEnd) : CommandOutcome
{
    public DateTimeOffset? CycleStart { get; } = CycleStart?.ToUniversalTime();
    public DateTimeOffset? CycleEnd { get; } = CycleEnd?.ToUniversalTime();
}

public sealed partial record NotesResource(int Current, int Maximum, DateTimeOffset? RecoveryAt)
{
    public DateTimeOffset? RecoveryAt { get; } = RecoveryAt?.ToUniversalTime();
}

public sealed partial record NotesCounter(int Current, int Maximum, DateTimeOffset? ResetAt)
{
    public DateTimeOffset? ResetAt { get; } = ResetAt?.ToUniversalTime();
}

public sealed partial record ExpeditionNotes(int Current, int Maximum, ExpeditionOutcome State, DateTimeOffset? CompletionAt)
{
    public DateTimeOffset? CompletionAt { get; } = CompletionAt?.ToUniversalTime();
}

public sealed partial record TransformerNotes(bool IsReady, DateTimeOffset ResetAt)
{
    public DateTimeOffset ResetAt { get; } = ResetAt.ToUniversalTime();
}

public sealed partial record GenshinNotesOutcome(string GameUid, DateTimeOffset ObservedAt, NotesResource Resin, ExpeditionNotes Expeditions, NotesResource HomeCoin, int RemainingWeeklyDiscounts, int WeeklyDiscountLimit, DateTimeOffset WeeklyResetAt, TransformerNotes? Transformer) : CommandOutcome
{
    public DateTimeOffset ObservedAt { get; } = ObservedAt.ToUniversalTime();
    public DateTimeOffset WeeklyResetAt { get; } = WeeklyResetAt.ToUniversalTime();
}

public sealed partial record HsrNotesOutcome(string GameUid, DateTimeOffset ObservedAt, NotesResource TrailblazePower, int EchoesOfWarRemaining, int EchoesOfWarLimit, DateTimeOffset EchoesOfWarResetAt, NotesCounter SimulatedUniverse, NotesCounter CurrencyWars) : CommandOutcome
{
    public DateTimeOffset ObservedAt { get; } = ObservedAt.ToUniversalTime();
    public DateTimeOffset EchoesOfWarResetAt { get; } = EchoesOfWarResetAt.ToUniversalTime();
}

public sealed partial record ZzzNotesOutcome(string GameUid, DateTimeOffset ObservedAt, NotesResource Battery, NotesCounter DailyEngagement, ScratchCardOutcome ScratchCard, VideoStoreOutcome VideoStore, NotesCounter? BountyCommission, NotesCounter? WeeklyPoints, NotesCounter? Omnicoins, TempleAdventureOutcome Adventure, TempleCraftingOutcome Crafting, TempleSalesOutcome Sales) : CommandOutcome
{
    public DateTimeOffset ObservedAt { get; } = ObservedAt.ToUniversalTime();
}

public sealed partial record RedemptionItem(string Code, RedemptionOutcome Outcome, CommandError? Error)
{
}

public sealed partial record CodeRedemptionOutcome(Game Game, string GameUid, OperationOutcome State, ImmutableArray<RedemptionItem> Items) : CommandOutcome
{
}

public sealed partial record CheckInItem(Game Game, CheckInOutcome Outcome, CommandError? Error)
{
}

public sealed partial record DailyCheckInOutcome(OperationOutcome State, ImmutableArray<CheckInItem> Items) : CommandOutcome
{
}
