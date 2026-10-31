using System.Diagnostics.CodeAnalysis;

namespace Mehrak.Domain.Command.Models;

/// <summary>A business result. A failed command may retain completed and unattempted item outcomes.</summary>
public sealed record CommandResult
{
    [MemberNotNullWhen(true, nameof(Outcome))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }
    public CommandFailureReason FailureReason { get; }
    public CommandError? Error { get; }
    public CommandOutcome? Outcome { get; }

    private CommandResult(bool isSuccess, CommandFailureReason failureReason, CommandError? error, CommandOutcome? outcome)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        Error = error;
        Outcome = outcome;
    }

    public static CommandResult Success(CommandOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new(true, CommandFailureReason.Unknown, null, outcome);
    }

    public static CommandResult Failure(CommandFailureReason failureReason, CommandError error, CommandOutcome? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, failureReason, error, outcome);
    }

    public static CommandResult Failure(CommandFailureReason failureReason, CommandErrorCode code,
        CommandErrorTarget target = CommandErrorTarget.None, CommandOutcome? outcome = null) =>
        Failure(failureReason, new CommandError(code, target), outcome);
}
