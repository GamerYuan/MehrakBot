#region

using Mehrak.Domain.Models;

#endregion

namespace Mehrak.Domain.Repositories;

public interface IUserRepository
{
    Task<UserModel?> GetUserAsync(ulong userId);

    Task CreateOrUpdateUserAsync(UserModel user);

    Task<bool> DeleteUserAsync(ulong userId);
}