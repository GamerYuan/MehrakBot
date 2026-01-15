using Mehrak.Dashboard.Auth;
using Mehrak.Dashboard.Models;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Mehrak.Dashboard.Services;

public interface IDashboardApplicationExecutorService<TContext> where TContext : IApplicationContext
{
    ulong DiscordUserId { get; set; }
    TContext ApplicationContext { get; set; }

    void AddValidator<TParam>(string paramName, Predicate<TParam> predicate, string? errorMessage = null);

    Task<DashboardApplicationExecutionResult> ExecuteAsync(
        int profileId,
        CancellationToken ct = default);
}

internal class DashboardApplicationExecutorService<TContext> : IDashboardApplicationExecutorService<TContext>
    where TContext : IApplicationContext
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly IDashboardProfileAuthenticationService m_ProfileAuthenticationService;
    private readonly ILogger<DashboardApplicationExecutorService<TContext>> m_Logger;
    private readonly List<ParamValidator> m_Validators = [];

    public DashboardApplicationExecutorService(
        IServiceProvider serviceProvider,
        IDashboardProfileAuthenticationService profileAuthenticationService,
        ILogger<DashboardApplicationExecutorService<TContext>> logger)
    {
        m_ServiceProvider = serviceProvider;
        m_ProfileAuthenticationService = profileAuthenticationService;
        m_Logger = logger;
    }

    public ulong DiscordUserId { get; set; }

    public TContext ApplicationContext { get; set; } = default!;

    public void AddValidator<TParam>(string paramName, Predicate<TParam> predicate, string? errorMessage = null)
    {
        m_Validators.Add(new ParamValidator<TParam>(paramName, predicate, errorMessage));
    }

    public async Task<DashboardApplicationExecutionResult> ExecuteAsync(
        int profileId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (ApplicationContext is null)
            throw new InvalidOperationException("ApplicationContext must be provided before executing.");
        if (DiscordUserId == 0)
            throw new InvalidOperationException("Discord user ID must be provided before executing.");

        var invalid = m_Validators.Where(v => !v.IsValid(ApplicationContext)).Select(v => v.ErrorMessage).ToArray();
        if (invalid.Length > 0)
        {
            m_Logger.LogWarning(
                "Dashboard application execution validation failed for user {UserId}: {Errors}",
                DiscordUserId,
                string.Join(", ", invalid));
            return DashboardApplicationExecutionResult.ValidationFailed(invalid);
        }

        var authResult = await m_ProfileAuthenticationService
            .AuthenticateAsync(DiscordUserId, profileId, null, ct)
            .ConfigureAwait(false);

        switch (authResult.Status)
        {
            case DashboardAuthStatus.Success:
                return await ExecuteApplicationAsync(authResult, ct).ConfigureAwait(false);
            case DashboardAuthStatus.PassphraseRequired:
                m_Logger.LogInformation(
                    "Dashboard authentication cache miss for user {UserId}, profile {ProfileId}",
                    DiscordUserId,
                    profileId);
                return DashboardApplicationExecutionResult.AuthenticationRequired(authResult.Error ??
                    "Authentication expired. Please re-authenticate this profile.");
            case DashboardAuthStatus.InvalidPassphrase:
                return DashboardApplicationExecutionResult.AuthenticationFailed(authResult.Error ??
                    "Invalid passphrase. Please try again.");
            case DashboardAuthStatus.NotFound:
                return DashboardApplicationExecutionResult.NotFound(authResult.Error ??
                    "Requested profile could not be found.");
            default:
                return DashboardApplicationExecutionResult.Error(authResult.Error ??
                    "Unable to complete authentication.");
        }
    }

    private async Task<DashboardApplicationExecutionResult> ExecuteApplicationAsync(
        DashboardProfileAuthenticationResult authResult,
        CancellationToken ct)
    {
        ApplicationContext.LtUid = authResult.LtUid;
        ApplicationContext.LToken = authResult.LToken!;

        m_Logger.LogInformation(
            "Executing dashboard application service for user {UserId}, profile {LtUid}",
            DiscordUserId,
            authResult.LtUid);

        var applicationService = m_ServiceProvider.GetRequiredService<IApplicationService<TContext>>();
        var commandResult = await applicationService.ExecuteAsync(ApplicationContext).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        return DashboardApplicationExecutionResult.FromCommandResult(commandResult);
    }
}

public enum DashboardExecutionStatus
{
    Success,
    ValidationFailed,
    AuthenticationRequired,
    AuthenticationFailed,
    NotFound,
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

    public static DashboardApplicationExecutionResult Error(string message)
    {
        return new DashboardApplicationExecutionResult(DashboardExecutionStatus.Error, errorMessage: message);
    }

    public IActionResult MapToActionResult()
    {
        return Status switch
        {
            DashboardExecutionStatus.Success when CommandResult is not null =>
                TryExtractAttachment(CommandResult) is { } attachment
                    ? new ObjectResult(attachment)
                    {
                        StatusCode = StatusCodes.Status200OK
                    }
                    : HandleMissingAttachment(CommandResult),
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
            _
                => new ObjectResult(new { error = ErrorMessage ?? "Unable to execute command." })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                }
        };
    }

    private static DashboardCommandAttachmentDto? TryExtractAttachment(CommandResult commandResult)
    {
        if (commandResult.Data is null)
            return null;

        var fileName = (commandResult.Data.Components.FirstOrDefault(x => x is CommandAttachment) as CommandAttachment)?.FileName;

        return fileName == null ? null : new(fileName);
    }

    private static ObjectResult HandleMissingAttachment(CommandResult commandResult)
    {
        string errorMessage;
        if (commandResult.IsSuccess)
        {
            errorMessage = (commandResult.Data.Components.FirstOrDefault(x => x is CommandText) as CommandText)
                ?.Content ?? "Command executed successfully but no attachment was found.";
        }
        else
        {
            errorMessage = commandResult.ErrorMessage ?? "Command execution failed without a specific error message.";
        }

        return new ObjectResult(new { error = errorMessage })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
    }
}
