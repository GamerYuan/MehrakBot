#region

using Mehrak.Domain.Models;

#endregion

namespace Mehrak.Domain.Repositories;

public interface IUserRepository
{
    Task<UserDto?> GetUserAsync(long userId);

    Task CreateOrUpdateUserAsync(UserDto user);

    Task<bool> DeleteUserAsync(long userId);
}
