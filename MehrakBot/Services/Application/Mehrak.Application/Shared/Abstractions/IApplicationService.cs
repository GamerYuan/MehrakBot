using Mehrak.Domain.Command.Models;

namespace Mehrak.Application.Shared.Abstractions;

public interface IApplicationService
{
    Task<CommandResult> ExecuteAsync(IApplicationContext context, CancellationToken cancellationToken = default);
}
