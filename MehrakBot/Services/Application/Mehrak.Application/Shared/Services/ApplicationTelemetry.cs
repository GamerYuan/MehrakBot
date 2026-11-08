using System.Diagnostics;

namespace Mehrak.Application.Shared.Services;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "Mehrak.Application";

    public const string CommandOutcomeTag = "mehrak.command.outcome";

    public const string CommandFailureReasonTag = "mehrak.command.failure_reason";

    public const string CommandErrorCodeTag = "mehrak.command.error_code";

    public const string SuccessOutcome = "success";

    public const string ExpectedFailureOutcome = "expected_failure";

    public const string FailureOutcome = "failure";

    public const string CancelledOutcome = "cancelled";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
