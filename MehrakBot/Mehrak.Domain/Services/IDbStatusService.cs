namespace Mehrak.Domain.Services;

public interface IDbStatusService
{
    Task<bool> GetDbStatus();
}
