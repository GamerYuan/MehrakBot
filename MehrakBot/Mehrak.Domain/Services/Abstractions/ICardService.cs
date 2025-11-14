#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICardService<TContext, TData, TServer> where
    TContext : ICardGenerationContext<TData, TServer> where TServer : Enum
{
    Task<Stream> GetCardAsync(TContext context);
}

public interface ICardService<TContext, TData>
    : ICardService<TContext, TData, Server> where TContext : ICardGenerationContext<TData>;

public interface ICardService<TData>
    : ICardService<ICardGenerationContext<TData>, TData>;
