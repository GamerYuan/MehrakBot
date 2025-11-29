#region

using Mehrak.Domain.Models;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface IApplicationService<TContext> where TContext : IApplicationContext
{
    Task<CommandResult> ExecuteAsync(TContext context);
}