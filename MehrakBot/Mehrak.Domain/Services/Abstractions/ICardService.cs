#region

using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICardService<TContext, TData> where
    TContext : ICardGenerationContext<TData>
{
    Task<Stream> GetCardAsync(TContext context);
}
public interface ICardService<TData>
    : ICardService<ICardGenerationContext<TData>, TData>;
