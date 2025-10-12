using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICardService<TContext, TData> where TContext : ICardGenerationContext<TData>
{
    public Task<Stream> GetCardAsync(TContext context);
}

public interface ICardService<TData>
{
    public Task<Stream> GetCardAsync(ICardGenerationContext<TData> context);
}
