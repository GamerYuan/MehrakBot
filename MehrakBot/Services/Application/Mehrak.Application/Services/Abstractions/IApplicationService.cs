using Mehrak.Domain.Command.Models;

namespace Mehrak.Application.Services.Abstractions;

public interface IApplicationService
{
    Task<CommandResult> ExecuteAsync(IApplicationContext context, CancellationToken cancellationToken = default);
}
