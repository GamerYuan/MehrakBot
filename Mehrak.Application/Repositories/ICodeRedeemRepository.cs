using Mehrak.Domain.Enums;

namespace Mehrak.Application.Repositories;

public interface ICodeRedeemRepository
{
    Task<List<string>> GetCodesAsync(GameName gameName);
    Task AddCodesAsync(GameName gameName, Dictionary<string, CodeStatus> codes);
}
