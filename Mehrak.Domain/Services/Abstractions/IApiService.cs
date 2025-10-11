using Mehrak.Domain.Models;

namespace Mehrak.Domain.Services.Abstractions;

public interface IApiService<TResult, TContext> where TContext : IApiContext
{
    public Task<Result<TResult>> GetAsync(TContext context);
}
