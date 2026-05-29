#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface IApiService
{
    const int MaxTimeoutSeconds = 30;
}

public interface IApiService<TResult, TContext> : IApiService where TContext : IApiContext
{
    Task<Result<TResult>> GetAsync(TContext context, CancellationToken cancellationToken = default);
}