using System.Security.Claims;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Dashboard.Shared.Models;
using Mehrak.Domain.Command.Extensions;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Dashboard.Shared.Services;

public interface IDashboardApplicationExecutorService
{
    ulong DiscordUserId { get; set; }
    string CommandName { get; set; }
    IReadOnlyDictionary<string, object> Parameters { get; set; }

    void AddValidator<TParam>(string paramName, Predicate<TParam> predicate, string? errorMessage = null);

    Task<DashboardApplicationExecutionResult> ExecuteAsync(
        int profileId,
        CancellationToken ct = default);
}

internal class DashboardApplicationExecutorService : IDashboardApplicationExecutorService
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IDashboardProfileAuthenticationService m_ProfileAuthenticationService;
    private readonly UserDbContext m_UserContext;
    private readonly ILogger<DashboardApplicationExecutorService> m_Logger;
    private readonly IHttpContextAccessor m_HttpContextAccessor;
    private readonly List<ParamValidator> m_Validators = [];

    public DashboardApplicationExecutorService(
        IServiceProvider serviceProvider,
        IDashboardProfileAuthenticationService profileAuthenticationService,
        UserDbContext userContext,
        ILogger<DashboardApplicationExecutorService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        m_ServiceProvider = serviceProvider;
        m_ProfileAuthenticationService = profileAuthenticationService;
        m_UserContext = userContext;
        m_Logger = logger;
        m_HttpContextAccessor = httpContextAccessor;
    }

    public ulong DiscordUserId { get; set; }

    public string CommandName { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

    public void AddValidator<TParam>(string paramName, Predicate<TParam> predicate, string? errorMessage = null)
    {
        m_Validators.Add(new ParamValidator<TParam>(paramName, predicate, errorMessage));
    }

    public async Task<DashboardApplicationExecutionResult> ExecuteAsync(
        int profileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (DiscordUserId == 0)
            throw new InvalidOperationException("Discord user ID must be provided before executing.");
        if (string.IsNullOrWhiteSpace(CommandName))
            throw new InvalidOperationException("Command name must be provided before executing.");

        var invalid = m_Validators.Where(v => !v.IsValid(Parameters)).Select(v => v.ErrorMessage).ToArray();
        if (invalid.Length > 0)
        {
            m_Logger.LogWarning(
                "Dashboard application execution validation failed for user {UserId}: {Errors}",
                DiscordUserId,
                string.Join(", ", invalid));
            return DashboardApplicationExecutionResult.ValidationFailed(invalid);
        }

        // Unlock tickets are bound to the owning login session, so the current
        // session claim must travel with the request. Without it the stored
        // ticket never matches and every command demands re-authentication.
        var sessionToken = m_HttpContextAccessor.HttpContext?.User.FindFirstValue("dashboard_session");

        var authResult = await m_ProfileAuthenticationService
            .AuthenticateAsync(DiscordUserId, profileId, null, ct, sessionToken)
            .ConfigureAwait(false);

        return authResult.Status switch
        {
            DashboardAuthStatus.Success => await ExecuteApplicationAsync(authResult, profileId, ct).ConfigureAwait(false),
            DashboardAuthStatus.PassphraseRequired => DashboardApplicationExecutionResult.AuthenticationRequired(authResult.Error ??
                                "Authentication expired. Please re-authenticate this profile."),
            DashboardAuthStatus.InvalidPassphrase => DashboardApplicationExecutionResult.AuthenticationFailed(authResult.Error ??
                                "Invalid passphrase. Please try again."),
            DashboardAuthStatus.RateLimited => DashboardApplicationExecutionResult.RateLimited(authResult.Error ??
                                "Too many attempts. Please try again later."),
            DashboardAuthStatus.NotFound => DashboardApplicationExecutionResult.NotFound(authResult.Error ??
                                "Requested profile could not be found."),
            _ => DashboardApplicationExecutionResult.Error(authResult.Error ??
                                "Unable to complete authentication."),
        };
    }

    private async Task<DashboardApplicationExecutionResult> ExecuteApplicationAsync(
        DashboardProfileAuthenticationResult authResult,
        int profileId,
        CancellationToken ct)
    {
        // Note: ApplicationContext set logic removed as we use direct params now
        // If we need to pass LtUid/LToken to gRPC, we pass them in request.

        m_Logger.LogInformation(
            "Executing dashboard application service for user {UserId}, profile {LtUid}, command {Command}",
            DiscordUserId,
            authResult.LtUid,
            CommandName);

        var applicationService = m_ServiceProvider.GetRequiredService<Proto.ApplicationService.ApplicationServiceClient>();

        var request = ProtobufMappingExtensions.ToExecuteRequest(
            CommandName,
            DiscordUserId,
            authResult.LtUid,
            authResult.LToken!,
            Parameters.Select(kv => (kv.Key, kv.Value))
        );

        var response = await applicationService.ExecuteCommandAsync(request, cancellationToken: ct)
            .ConfigureAwait(false);

        var commandResult = response.ToDomain();

        ct.ThrowIfCancellationRequested();

        if (commandResult.IsSuccess)
            await UpdateLastUsedServerAsync(authResult, profileId, ct).ConfigureAwait(false);

        return DashboardApplicationExecutionResult.FromCommandResult(commandResult);
    }

    private async Task UpdateLastUsedServerAsync(DashboardProfileAuthenticationResult authResult, int profileId, CancellationToken ct)
    {
        if (authResult.User?.Profiles == null) return;
        if (!Parameters.TryGetValue("game", out var gameObj) || gameObj is not Game game) return;
        if (!Parameters.TryGetValue("server", out var serverObj) || serverObj is not string server) return;

        var profileDto = authResult.User.Profiles.FirstOrDefault(p => p.ProfileId == profileId);
        if (profileDto == null) return;

        var profile = await m_UserContext.UserProfiles
            .Where(p => p.Id == profileDto.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (profile == null) return;

        var region = await m_UserContext.Regions
            .Where(x => x.ProfileId == profile.Id && x.Game == game)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        try
        {
            if (region == null)
            {
                await m_UserContext.Regions.AddAsync(new ProfileRegion
                {
                    ProfileId = profile.Id,
                    Game = game,
                    Region = server
                }, ct).ConfigureAwait(false);
            }
            else
            {
                region.Region = server;
                m_UserContext.Update(region);
            }

            await m_UserContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException e)
        {
            m_Logger.LogError(e, "Failed to update last used server for user {UserId}, profile {ProfileId}, game {Game}",
                DiscordUserId, profileDto.ProfileId, game);
        }
    }
}

public enum DashboardExecutionStatus
{
    Success,
    ValidationFailed,
    AuthenticationRequired,
    AuthenticationFailed,
    NotFound,
    RateLimited,
    Error
}

public sealed class DashboardApplicationExecutionResult
{
    private DashboardApplicationExecutionResult(
        DashboardExecutionStatus status,
        CommandResult? commandResult = null,
        string? errorMessage = null,
        IReadOnlyCollection<string>? validationErrors = null)
    {
        Status = status;
        CommandResult = commandResult;
        ErrorMessage = errorMessage;
        ValidationErrors = validationErrors ?? [];
    }

    public DashboardExecutionStatus Status { get; }
    public bool IsSuccess => Status == DashboardExecutionStatus.Success;
    public CommandResult? CommandResult { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyCollection<string> ValidationErrors { get; }

    public static DashboardApplicationExecutionResult FromCommandResult(CommandResult result)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.Success, result);
    }

    public static DashboardApplicationExecutionResult ValidationFailed(IEnumerable<string> errors)
    {
        var errorList = errors.ToArray();
        return new DashboardApplicationExecutionResult(
            DashboardExecutionStatus.ValidationFailed,
            validationErrors: errorList,
            errorMessage: "One or more parameters failed validation.");
    }

    public static DashboardApplicationExecutionResult AuthenticationRequired(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.AuthenticationRequired, errorMessage: message);
    }

    public static DashboardApplicationExecutionResult AuthenticationFailed(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.AuthenticationFailed, errorMessage: message);
    }

    public static DashboardApplicationExecutionResult NotFound(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.NotFound, errorMessage: message);
    }

    public static DashboardApplicationExecutionResult RateLimited(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.RateLimited, errorMessage: message);
    }

    public static DashboardApplicationExecutionResult Error(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.Error, errorMessage: message);
    }

    public IActionResult MapToActionResult()
    {
        return Status switch
        {
            DashboardExecutionStatus.Success when CommandResult is not null => MapCommandResult(CommandResult),
            DashboardExecutionStatus.ValidationFailed
                => new ObjectResult(new { error = ErrorMessage ?? "Validation failed.", validationErrors = ValidationErrors })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                },
            DashboardExecutionStatus.AuthenticationRequired
                => new ObjectResult(new { error = ErrorMessage ?? "Authentication required.", code = "AUTH_REQUIRED" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                },
            DashboardExecutionStatus.AuthenticationFailed
                => new ObjectResult(new { error = ErrorMessage ?? "Authentication failed." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                },
            DashboardExecutionStatus.NotFound
                => new ObjectResult(new { error = ErrorMessage ?? "Requested resource was not found." })
                {
                    StatusCode = StatusCodes.Status404NotFound
                },
            DashboardExecutionStatus.RateLimited
                => new ObjectResult(new { error = ErrorMessage ?? "Too many attempts. Please try again later." })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                },
            _
                => new ObjectResult(new { error = ErrorMessage ?? "Unable to execute command." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                }
        };
    }

    private static ObjectResult MapCommandResult(CommandResult commandResult)
    {
        try
        {
            // Also validate callers supplying a domain result directly, not just gRPC responses.
            var validated = commandResult.ToProto().ToDomain();
            if (!validated.IsSuccess)
                return CommandFailure(validated.Error ?? new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None));

            DashboardCommandAttachmentDto response = validated.Outcome switch
            {
                CharacterCardsOutcome cards => MapCharacters(cards),
                RosterOutcome roster when roster.State is OperationOutcome.Complete or OperationOutcome.Empty =>
                    new DashboardRosterDto(AttachmentKey(roster.Image), roster.Game, roster.GameUid, roster.State),
                EndgameOutcome endgame when endgame.State is OperationOutcome.Complete or OperationOutcome.Empty or OperationOutcome.NotUnlocked =>
                    new DashboardEndgameDto(AttachmentKey(endgame.Image), endgame.Game, endgame.GameUid, endgame.State,
                        endgame.Mode, endgame.Floor, endgame.CycleStart, endgame.CycleEnd),
                // No notes, redemption or check-in HTTP routes: do not coerce these into cards.
                _ => throw new ArgumentException("Unsupported dashboard outcome.")
            };
            return new ObjectResult(response) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        {
            return CommandFailure(new CommandError(CommandErrorCode.InvalidResponse, CommandErrorTarget.None));
        }
    }

    private static DashboardCharacterCardsDto MapCharacters(CharacterCardsOutcome cards)
    {
        if (cards.State is not (OperationOutcome.Complete or OperationOutcome.Empty or OperationOutcome.Partial or
            OperationOutcome.RequestLimitExceeded or OperationOutcome.Failed))
            throw new ArgumentException("Unsupported character state.");

        var items = cards.Items.Select(item => new DashboardCharacterCardItemDto(
            item.RequestedName, item.CharacterName, item.CharacterId, item.Status, AttachmentKey(item.Image),
            item.Error is { } error ? new DashboardCommandErrorDto(error.Message, error.Code) : null)).ToArray();
        return new DashboardCharacterCardsDto(items.FirstOrDefault(item => item.StorageFileName is not null)?.StorageFileName,
            cards.Game, cards.GameUid, cards.State,
            cards.State == OperationOutcome.RequestLimitExceeded ? CommandErrorCode.RequestLimitExceeded : null,
            items, cards.RequestLimit);
    }

    private static string? AttachmentKey(StorageReference? image)
    {
        if (image is null) return null;
        // The existing download endpoint reads attachment storage, not static image storage.
        if (image.SourceType != AttachmentSourceType.AttachmentStorage || string.IsNullOrWhiteSpace(image.StorageFileName))
            throw new ArgumentException("Invalid dashboard card reference.");
        return image.StorageFileName;
    }

    private static ObjectResult CommandFailure(CommandError error) =>
        new(new DashboardCommandErrorDto(error.Message, error.Code))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
}
