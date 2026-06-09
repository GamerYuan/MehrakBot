namespace Mehrak.Domain.Shared.Services;

public interface IDbStatusService
{
    Task<bool> GetDbStatus();
}
