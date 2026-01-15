using Mehrak.Domain.Models;
using Mehrak.Infrastructure.Models;

namespace Mehrak.Infrastructure.Extensions;

internal static class UserDtoExtensions
{
    public static UserDto ToDto(this UserModel model)
    {
        var dto = new UserDto
        {
            Id = (ulong)model.Id,
            Timestamp = model.Timestamp,
            Profiles = [.. model.Profiles.Select(p => new UserProfileDto
            {
                ProfileId = p.ProfileId,
                LtUid = (ulong)p.LtUid,
                LToken = p.LToken,
                LastCheckIn = p.LastCheckIn,
                GameUids = p.GameUids
                        .GroupBy(g => g.Game)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToDictionary(x => x.Region, x => x.GameUid)
                        ),
                LastUsedRegions = p.LastUsedRegions.DistinctBy(r => r.Game).ToDictionary(r => r.Game, r => r.Region)
            })]
        };
        return dto;
    }

    public static UserModel ToUserModel(this UserDto dto)
    {
        var model = new UserModel
        {
            Id = (long)dto.Id,
            Timestamp = dto.Timestamp,
            Profiles = dto.BuildProfileModels(dto.Id)
        };
        return model;
    }

    public static List<UserProfileModel> BuildProfileModels(this UserDto dto, ulong userId)
    {
        var profiles = new List<UserProfileModel>();
        if (dto.Profiles == null)
            return profiles;

        foreach (var p in dto.Profiles)
        {
            var profile = new UserProfileModel
            {
                UserId = (long)userId,
                ProfileId = p.ProfileId,
                LtUid = (long)p.LtUid,
                LToken = p.LToken,
                LastCheckIn = p.LastCheckIn,
                GameUids = [],
                LastUsedRegions = []
            };

            foreach (var gameEntry in p.GameUids)
            {
                var game = gameEntry.Key;
                foreach (var regionEntry in gameEntry.Value)
                {
                    profile.GameUids.Add(new ProfileGameUid
                    {
                        Game = game,
                        Region = regionEntry.Key,
                        GameUid = regionEntry.Value
                    });
                }
            }

            foreach (var region in p.LastUsedRegions)
            {
                profile.LastUsedRegions.Add(new ProfileRegion
                {
                    Game = region.Key,
                    Region = region.Value
                });
            }

            profiles.Add(profile);
        }

        return profiles;
    }
}
