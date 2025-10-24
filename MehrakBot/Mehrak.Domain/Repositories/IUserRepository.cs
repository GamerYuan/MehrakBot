using Mehrak.Domain.Models;

namespace Mehrak.Domain.Repositories;

public interface IUserRepository
{
    public Task<UserModel?> GetUserAsync(ulong userId);

    public Task CreateOrUpdateUserAsync(UserModel user);

    public Task<bool> DeleteUserAsync(ulong userId);
}
