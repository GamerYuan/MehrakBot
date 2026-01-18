using Mehrak.Domain.Models;

namespace Mehrak.Domain.Services.Abstractions;

public interface ISystemResourceClientService
{
    ValueTask<SystemResource> GetSystemResourceAsync();
}
