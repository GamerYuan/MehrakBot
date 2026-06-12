using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character;

public interface IUserPortraitService
{
    Task<IReadOnlyCollection<UserPortraitUploadDto>> GetUserPortraitsAsync(long discordUserId, Game game, string? characterName, CancellationToken ct = default);
    Task<UserPortraitUploadDto?> GetPortraitAsync(long discordUserId, Guid uploadId, CancellationToken ct = default);
    Task<UploadPortraitResult> UploadPortraitAsync(long discordUserId, Game game, string characterName, Stream imageStream, string sha256, string extension, CancellationToken ct = default);
    Task<bool> UpdatePortraitConfigAsync(long discordUserId, Guid uploadId, UserPortraitConfigDto config, CancellationToken ct = default);
    Task<bool> DeletePortraitAsync(long discordUserId, Guid uploadId, CancellationToken ct = default);
    Task<int> GetUploadCountAsync(long discordUserId, Game game, string characterName, CancellationToken ct = default);
}
