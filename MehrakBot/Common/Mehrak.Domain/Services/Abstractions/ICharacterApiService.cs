#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterApiService<TBasic, TDetail, TContext> where TContext : IApiContext
{
    Task<Result<IEnumerable<TBasic>>> GetAllCharactersAsync(TContext context, CancellationToken cancellationToken = default);

    Task<Result<TDetail>> GetCharacterDetailAsync(TContext context, CancellationToken cancellationToken = default);
}