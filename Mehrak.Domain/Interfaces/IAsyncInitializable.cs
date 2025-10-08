namespace Mehrak.Domain.Interfaces;

public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
