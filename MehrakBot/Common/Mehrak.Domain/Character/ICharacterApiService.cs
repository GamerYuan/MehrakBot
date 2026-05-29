#region

using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Models;

#endregion

namespace Mehrak.Domain.Character;

public interface ICharacterApiService<TBasic, TDetail, TContext> where TContext : IApiContext
{
    Task<Result<IEnumerable<TBasic>>> GetAllCharactersAsync(TContext context, CancellationToken cancellationToken = default);

    Task<Result<TDetail>> GetCharacterDetailAsync(TContext context, CancellationToken cancellationToken = default);
}