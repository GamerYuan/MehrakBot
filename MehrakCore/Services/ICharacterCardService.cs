#region

using MehrakCore.ApiResponseTypes;

#endregion

namespace MehrakCore.Services;

public interface ICharacterCardService<T> where T : ICharacterInformation
{
    public Task<Stream> GenerateCharacterCardAsync(T characterInformation);
}
