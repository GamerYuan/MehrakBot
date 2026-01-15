#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Repositories;

public interface ICodeRedeemRepository
{
    Task<List<string>> GetCodesAsync(Game gameName);

    Task UpdateCodesAsync(Game gameName, Dictionary<string, CodeStatus> codes);
}