#region

using MehrakCore.ApiResponseTypes.Hsr;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

public class HsrCharacterCardService : ICharacterCardService<HsrCharacterInformation>
{
    public Task<Stream> GenerateCharacterCardAsync(HsrCharacterInformation characterInformation, string gameUid)
    {
        throw new NotImplementedException();
    }
}
