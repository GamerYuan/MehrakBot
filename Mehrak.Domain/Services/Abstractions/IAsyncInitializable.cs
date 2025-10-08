namespace Mehrak.Domain.Services.Abstractions;

public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
