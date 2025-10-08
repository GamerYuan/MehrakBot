#region

#endregion

using Mehrak.Domain.Models;

namespace Mehrak.Domain.Interfaces;

public interface ICardService<T> : IAsyncInitializable
{
    public Task<Stream> GenerateCharacterCardAsync(T data, GameProfileDto gameData);
}
