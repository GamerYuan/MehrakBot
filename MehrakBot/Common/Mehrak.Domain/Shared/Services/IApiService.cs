#region

using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Models;

#endregion

namespace Mehrak.Domain.Shared.Services;

public interface IApiService
{
    const int MaxTimeoutSeconds = 30;
}

public interface IApiService<TResult, TContext> : IApiService where TContext : IApiContext
{
    Task<Result<TResult>> GetAsync(TContext context, CancellationToken cancellationToken = default);
}
