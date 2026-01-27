using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;

namespace Mehrak.Application.Services.Zzz.Tower;

public class ZzzTowerCardService : ICardService<ZzzTowerData>
{
    public Task<Stream> GetCardAsync(ICardGenerationContext<ZzzTowerData> context)
    {
        throw new NotImplementedException();
    }
}
