#region

#endregion

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Application.Services;

public interface ICardService<T> : IAsyncInitializable
{
    public Task<Stream> GenerateCharacterCardAsync(T data, GameProfileDto gameData);
}
