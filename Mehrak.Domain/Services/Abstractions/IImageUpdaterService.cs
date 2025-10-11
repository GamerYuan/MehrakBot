namespace Mehrak.Domain.Services.Abstractions;

public interface IImageUpdaterService<T>
{
    public Task UpdateDataAsync(T characterInformation, IEnumerable<Dictionary<string, string>> wiki);

    public Task UpdateAvatarAsync(string avatarId, string avatarUrl);

    public Task UpdateSideAvatarAsync(string avatarId, string avatarUrl);
}
