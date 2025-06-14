#region

using MehrakCore.ApiResponseTypes;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICharacterCardService<in T> where T : ICharacterInformation
{
    public Task<Stream> GenerateCharacterCardAsync(T characterInformation, string gameUid);
}