#region

using Mehrak.Domain.User.Abstractions;

#endregion

namespace Mehrak.Domain.Card;

public interface ICardService<TData>
{
    Task<Stream> GetCardAsync(ICardGenerationContext<TData> context);
}
