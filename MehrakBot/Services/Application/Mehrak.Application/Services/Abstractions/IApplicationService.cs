using Mehrak.Domain.Models;

namespace Mehrak.Application.Services.Abstractions;

public interface IApplicationService
{
    Task<CommandResult> ExecuteAsync(IApplicationContext context);
}
