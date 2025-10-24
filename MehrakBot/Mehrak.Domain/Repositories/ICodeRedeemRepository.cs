using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Repositories;

public interface ICodeRedeemRepository
{
    Task<List<string>> GetCodesAsync(Game gameName);

    Task AddCodesAsync(Game gameName, Dictionary<string, CodeStatus> codes);
}
