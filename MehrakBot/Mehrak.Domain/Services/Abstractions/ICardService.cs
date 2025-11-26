#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICardService<TData>
{
    Task<Stream> GetCardAsync(ICardGenerationContext<TData> context);
}
