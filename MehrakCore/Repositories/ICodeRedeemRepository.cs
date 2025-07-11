#region

using MehrakCore.Models;

#endregion

namespace MehrakCore.Repositories;

public interface ICodeRedeemRepository
{
    public Task<List<string>> GetCodesAsync(GameName gameName);

    public Task AddCodesAsync(GameName gameName, List<string> codes);
}
