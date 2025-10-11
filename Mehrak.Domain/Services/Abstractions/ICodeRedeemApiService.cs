using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICodeRedeemApiService
{
    public Task<Result<(string, CodeStatus)>> RedeemCodeAsync(Game game,
        string code, ulong ltuid, string ltoken, string gameUid, string region);
}
