namespace Mehrak.Domain.Shared.Services;

public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
