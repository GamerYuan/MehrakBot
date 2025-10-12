using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterApiService<TBasic, TDetail, TContext> where TContext : IApiContext
{
    public Task<Result<IEnumerable<TBasic>>> GetAllCharactersAsync(TContext context);

    public Task<Result<TDetail>> GetCharacterDetailAsync(TContext context);
}
