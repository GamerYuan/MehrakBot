#region

using MehrakCore.ApiResponseTypes;

#endregion

namespace Mehrak.Domain.Interfaces;

public interface ICharacterCardService<in T> : IAsyncInitializable where T : ICharacterInformation
{
    public Task<Stream> GenerateCharacterCardAsync(T characterInformation, string gameUid);
}
