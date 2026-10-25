namespace Mehrak.Bot.Shared.Localization;

/// <summary>
/// Symbolic keys for command UI text. Every constant must exist in the neutral resource file
/// (en.json); a parity test enforces that all supported locales contain the same keys with the
/// same placeholders. Discord metadata and help descriptions are keyed by their English source
/// text instead and are therefore not listed here.
/// </summary>
internal static class CommandUiKeys
{
    // Command execution
    internal const string ExecutorProfileOutOfRange = "Executor.ProfileOutOfRange";
    internal const string ExecutorValidationFailed = "Executor.ValidationFailed";
    internal const string ExecutorValidationFloor = "Executor.Validation.Floor";
    internal const string ExecutorValidationCharacterEmpty = "Executor.Validation.CharacterEmpty";
    internal const string ExecutorServerRequired = "Executor.ServerRequired";
    internal const string ExecutorCompleted = "Executor.Completed";
    internal const string ExecutorRemoveButton = "Executor.RemoveButton";
    internal const string ExecutorUnexpectedError = "Executor.UnexpectedError";

    // Framework result handler
    internal const string FrameworkInputInvalid = "Framework.InputInvalid";
    internal const string FrameworkUnexpectedError = "Framework.UnexpectedError";

    // Rate limiting
    internal const string RateLimitUnavailable = "RateLimit.Unavailable";
    internal const string RateLimitTooFrequent = "RateLimit.TooFrequent";

    // Authentication middleware
    internal const string AuthUserNotFound = "Auth.UserNotFound";
    internal const string AuthNoProfiles = "Auth.NoProfiles";
    internal const string AuthTooManyAttempts = "Auth.TooManyAttempts";
    internal const string AuthIncorrectPassphrase = "Auth.IncorrectPassphrase";
    internal const string AuthCorrupted = "Auth.Corrupted";
    internal const string AuthCredentialsChanged = "Auth.CredentialsChanged";
    internal const string AuthModalExpired = "Auth.ModalExpired";

    // Authentication modal and profile management
    internal const string AuthInvalidCookies = "Auth.InvalidCookies";
    internal const string AuthPassphraseLength = "Auth.PassphraseLength";
    internal const string AuthValidateFailed = "Auth.ValidateFailed";
    internal const string AuthNoSupportedProfiles = "Auth.NoSupportedProfiles";
    internal const string AuthTooManyProfiles = "Auth.TooManyProfiles";
    internal const string AuthProfileExists = "Auth.ProfileExists";
    internal const string AuthProfileAdded = "Auth.ProfileAdded";
    internal const string AuthAddFailed = "Auth.AddFailed";
    internal const string AuthUpdateFailed = "Auth.UpdateFailed";
    internal const string AuthProfileUpdated = "Auth.ProfileUpdated";
    internal const string AuthProcessingError = "Auth.ProcessingError";
    internal const string AuthProfileNotFound = "Auth.ProfileNotFound";
    internal const string ModalAuthTitle = "Modal.Auth.Title";
    internal const string ModalAuthUpdateTitle = "Modal.Auth.UpdateTitle";
    internal const string ModalCookieLabel = "Modal.CookieLabel";
    internal const string ModalPassphraseLabel = "Modal.PassphraseLabel";
    internal const string ModalCookiePlaceholder = "Modal.CookiePlaceholder";
    internal const string ModalNewPassphrasePlaceholder = "Modal.NewPassphrasePlaceholder";
    internal const string ModalPassphrasePlaceholder = "Modal.PassphrasePlaceholder";
    internal const string ModalProfileHeader = "Modal.ProfileHeader";
    internal const string ProfileDisplayHeader = "Profile.DisplayHeader";
    internal const string ProfileNoneFound = "Profile.NoneFound";
    internal const string ProfileAllDeleted = "Profile.AllDeleted";
    internal const string ProfileDeleteAllFailed = "Profile.DeleteAllFailed";
    internal const string ProfileNotFound = "Profile.NotFound";
    internal const string ProfileDeleted = "Profile.Deleted";
    internal const string ProfileDeleteFailed = "Profile.DeleteFailed";

    // HoYoLAB post embedding
    internal const string HylInvalidUrl = "Hyl.InvalidUrl";
    internal const string HylUnexpectedError = "Hyl.UnexpectedError";
    internal const string HylFetchFailed = "Hyl.FetchFailed";
    internal const string HylParseFailed = "Hyl.ParseFailed";

    // Safe error categories. Values mirror the neutral English CommandError.Message text;
    // raw exception or upstream messages are never used for these keys.
    internal const string ErrorAuthenticationRequired = "Error.AuthenticationRequired";
    internal const string ErrorApiUnavailable = "Error.ApiUnavailable";
    internal const string ErrorImageUpdateFailed = "Error.ImageUpdateFailed";
    internal const string ErrorAttachmentStoreFailed = "Error.AttachmentStoreFailed";
    internal const string ErrorCancelled = "Error.Cancelled";
    internal const string ErrorTimeout = "Error.Timeout";
    internal const string ErrorInvalidRequest = "Error.InvalidRequest";
    internal const string ErrorInvalidResponse = "Error.InvalidResponse";
    internal const string ErrorCharacterNotFound = "Error.CharacterNotFound";
    internal const string ErrorRequestLimitExceeded = "Error.RequestLimitExceeded";
    internal const string ErrorUnexpected = "Error.Unexpected";

    // Outcome presenter
    internal const string PresenterApiLimitationFooter = "Presenter.ApiLimitationFooter";
    internal const string PresenterNoCharacters = "Presenter.NoCharacters";
    internal const string PresenterCharacterLimit = "Presenter.CharacterLimit";
    internal const string PresenterCharacterNotFound = "Presenter.CharacterNotFound";
    internal const string PresenterNotAttemptedItem = "Presenter.NotAttemptedItem";
    internal const string PresenterItemLine = "Presenter.ItemLine";
    internal const string PresenterNoCodes = "Presenter.NoCodes";
    internal const string PresenterRedeemed = "Presenter.Redeemed";
    internal const string PresenterCodeExpired = "Presenter.CodeExpired";
    internal const string PresenterCodeInvalid = "Presenter.CodeInvalid";
    internal const string PresenterCodeCooldown = "Presenter.CodeCooldown";
    internal const string PresenterCodeAlreadyUsed = "Presenter.CodeAlreadyUsed";
    internal const string PresenterNotAttempted = "Presenter.NotAttempted";
    internal const string PresenterCodeError = "Presenter.CodeError";
    internal const string PresenterCheckInAlreadyDone = "Presenter.CheckInAlreadyDone";
    internal const string PresenterNoGameRecords = "Presenter.NoGameRecords";
    internal const string PresenterCheckInSuccess = "Presenter.CheckInSuccess";
    internal const string PresenterAlreadyCheckedIn = "Presenter.AlreadyCheckedIn";
    internal const string PresenterNoValidAccount = "Presenter.NoValidAccount";
    internal const string PresenterCheckInTimeout = "Presenter.CheckInTimeout";
    internal const string PresenterUnknownStatus = "Presenter.UnknownStatus";
    internal const string PresenterEndgameTitle = "Presenter.Endgame.Title";
    internal const string PresenterEndgameLocked = "Presenter.Endgame.Locked";
    internal const string PresenterEndgameEmpty = "Presenter.Endgame.Empty";
    internal const string PresenterEndgameFloor = "Presenter.Endgame.Floor";
    internal const string PresenterEndgameCycleStart = "Presenter.Endgame.CycleStart";
    internal const string PresenterEndgameCycleEnd = "Presenter.Endgame.CycleEnd";
    internal const string PresenterEndgameSpiralAbyss = "Presenter.Endgame.Mode.SpiralAbyss";
    internal const string PresenterEndgameImaginariumTheater = "Presenter.Endgame.Mode.ImaginariumTheater";
    internal const string PresenterEndgameStygianOnslaught = "Presenter.Endgame.Mode.StygianOnslaught";
    internal const string PresenterEndgameMemoryOfChaos = "Presenter.Endgame.Mode.MemoryOfChaos";
    internal const string PresenterEndgamePureFiction = "Presenter.Endgame.Mode.PureFiction";
    internal const string PresenterEndgameApocalypticShadow = "Presenter.Endgame.Mode.ApocalypticShadow";
    internal const string PresenterEndgameAnomalyArbitration = "Presenter.Endgame.Mode.AnomalyArbitration";
    internal const string PresenterEndgameShiyuDefense = "Presenter.Endgame.Mode.ShiyuDefense";
    internal const string PresenterEndgameDeadlyAssault = "Presenter.Endgame.Mode.DeadlyAssault";
    internal const string PresenterEndgameSimulatedBattleTrial = "Presenter.Endgame.Mode.SimulatedBattleTrial";

    // Real-time notes
    internal const string NotesReset = "Notes.Reset";
    internal const string NotesRecovers = "Notes.Recovers";
    internal const string NotesRefreshesIn = "Notes.RefreshesIn";
    internal const string NotesStateUnknown = "Notes.State.Unknown";
    internal const string NotesStateIncomplete = "Notes.State.Incomplete";
    internal const string NotesStateCompleted = "Notes.State.Completed";
    internal const string NotesStateWaitingToOpen = "Notes.State.WaitingToOpen";
    internal const string NotesStateOpen = "Notes.State.Open";
    internal const string NotesStateRevenueAvailable = "Notes.State.RevenueAvailable";
    internal const string NotesStateDispatchable = "Notes.State.Dispatchable";
    internal const string NotesStateAdventuring = "Notes.State.Adventuring";
    internal const string NotesStateCrafting = "Notes.State.Crafting";
    internal const string NotesStateCraftable = "Notes.State.Craftable";
    internal const string NotesStateNotSelling = "Notes.State.NotSelling";
    internal const string NotesStateSelling = "Notes.State.Selling";
    internal const string NotesStateOutOfStock = "Notes.State.OutOfStock";
    internal const string NotesGenshinTitle = "Notes.Genshin.Title";
    internal const string NotesGenshinResin = "Notes.Genshin.Resin";
    internal const string NotesGenshinExpeditions = "Notes.Genshin.Expeditions";
    internal const string NotesGenshinExpeditionNone = "Notes.Genshin.ExpeditionNone";
    internal const string NotesGenshinToBeDispatched = "Notes.Genshin.ToBeDispatched";
    internal const string NotesGenshinAllCompleted = "Notes.Genshin.AllCompleted";
    internal const string NotesGenshinCompletes = "Notes.Genshin.Completes";
    internal const string NotesGenshinSereniteaPot = "Notes.Genshin.SereniteaPot";
    internal const string NotesGenshinWeeklyBosses = "Notes.Genshin.WeeklyBosses";
    internal const string NotesGenshinWeeklyDiscount = "Notes.Genshin.WeeklyDiscount";
    internal const string NotesGenshinTransformer = "Notes.Genshin.Transformer";
    internal const string NotesGenshinNotClaimed = "Notes.Genshin.NotClaimed";
    internal const string NotesGenshinClaimed = "Notes.Genshin.Claimed";
    internal const string NotesGenshinAlreadyFull = "Notes.Genshin.AlreadyFull";
    internal const string NotesGenshinToBeCollected = "Notes.Genshin.ToBeCollected";
    internal const string NotesHsrTitle = "Notes.Hsr.Title";
    internal const string NotesHsrTrailblazePower = "Notes.Hsr.TrailblazePower";
    internal const string NotesHsrEchoesOfWar = "Notes.Hsr.EchoesOfWar";
    internal const string NotesHsrClaimed = "Notes.Hsr.Claimed";
    internal const string NotesHsrFullyClaimed = "Notes.Hsr.FullyClaimed";
    internal const string NotesHsrSimulatedUniverse = "Notes.Hsr.SimulatedUniverse";
    internal const string NotesHsrCurrencyWars = "Notes.Hsr.CurrencyWars";
    internal const string NotesZzzTitle = "Notes.Zzz.Title";
    internal const string NotesZzzBattery = "Notes.Zzz.Battery";
    internal const string NotesZzzFullyRecovered = "Notes.Zzz.FullyRecovered";
    internal const string NotesZzzDailyMissions = "Notes.Zzz.DailyMissions";
    internal const string NotesZzzDailyEngagement = "Notes.Zzz.DailyEngagement";
    internal const string NotesZzzScratchCard = "Notes.Zzz.ScratchCard";
    internal const string NotesZzzVideoStore = "Notes.Zzz.VideoStore";
    internal const string NotesZzzSeasonMissions = "Notes.Zzz.SeasonMissions";
    internal const string NotesZzzBountyCommission = "Notes.Zzz.BountyCommission";
    internal const string NotesZzzWeeklyPoints = "Notes.Zzz.WeeklyPoints";
    internal const string NotesZzzOmnicoins = "Notes.Zzz.Omnicoins";
    internal const string NotesZzzSuibianTemple = "Notes.Zzz.SuibianTemple";
    internal const string NotesZzzAdventure = "Notes.Zzz.Adventure";
    internal const string NotesZzzCraftingWorkshop = "Notes.Zzz.CraftingWorkshop";
    internal const string NotesZzzSalesStall = "Notes.Zzz.SalesStall";

    // Help rendering
    internal const string HelpSubcommands = "Help.Subcommands";
    internal const string HelpUsage = "Help.Usage";
    internal const string HelpParameters = "Help.Parameters";
    internal const string HelpOptional = "Help.Optional";
    internal const string HelpExamples = "Help.Examples";
    internal const string HelpOverviewFallback = "Help.OverviewFallback";
    internal const string HelpCommandFallback = "Help.CommandFallback";
    internal const string HelpAvailableCommands = "Help.AvailableCommands";
    internal const string HelpUsageHint = "Help.UsageHint";
    internal const string HelpFooter = "Help.Footer";
    internal const string HelpSubcommandLine = "Help.SubcommandLine";
    internal const string HelpParameterLine = "Help.ParameterLine";
    internal const string HelpAvailableRoot = "Help.AvailableRoot";
    internal const string HelpAvailableSub = "Help.AvailableSub";

    // Miscellaneous commands
    internal const string PingPong = "Ping.Pong";
    internal const string HealthReportTitle = "Health.ReportTitle";
    internal const string HealthSystemResources = "Health.SystemResources";
    internal const string HealthMonitorOffline = "Health.MonitorOffline";
    internal const string HealthSystemStatus = "Health.SystemStatus";
    internal const string HealthApiStatus = "Health.ApiStatus";
    internal const string HealthOnline = "Health.Online";
    internal const string HealthOffline = "Health.Offline";
    internal const string HealthMemory = "Health.Memory";
    internal const string HealthDiscordLatency = "Health.DiscordLatency";
}
