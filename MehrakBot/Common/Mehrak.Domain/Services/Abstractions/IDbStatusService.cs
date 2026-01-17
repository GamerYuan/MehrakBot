namespace Mehrak.Domain.Services.Abstractions;

public interface IDbStatusService
{
    Task<bool> GetDbStatus();
}
