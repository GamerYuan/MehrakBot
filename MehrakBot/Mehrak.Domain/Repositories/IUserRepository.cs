#region

using Mehrak.Domain.Models;

#endregion

namespace Mehrak.Domain.Repositories;

public interface IUserRepository
{
    Task<UserDto?> GetUserAsync(ulong userId);

    Task<bool> CreateOrUpdateUserAsync(UserDto user);

    Task<bool> DeleteUserAsync(ulong userId);
}
