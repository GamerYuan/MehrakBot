#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface IApiService<TResult, TContext> where TContext : IApiContext
{
    public Task<Result<TResult>> GetAsync(TContext context);
}