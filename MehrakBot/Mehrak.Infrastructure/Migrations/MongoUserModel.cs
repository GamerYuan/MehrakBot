using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace Mehrak.Infrastructure.Migrations;

[Obsolete]
public class MongoUserModel
{
    [BsonId] public ulong Id { get; set; }
    [BsonElement("ts")] public DateTime Timestamp { get; set; }
    [BsonElement("profiles")] public IEnumerable<MongoUserProfile>? Profiles { get; set; } = null;

    public UserDto ToDto()
    {
        return new UserDto()
        {
            Id = Id,
            Timestamp = Timestamp,
            Profiles = Profiles?.Select(x => x.ToDto()) ?? []
        };
    }
}

[Obsolete]
public class MongoUserProfile
{
    [BsonElement("profile_id")] public uint ProfileId { get; set; }
    [BsonElement("ltuid")] public ulong LtUid { get; set; }
    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;
    [BsonElement("last_checkin")] public DateTime? LastCheckIn { get; set; }

    [BsonElement("game_uids")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    [BsonRepresentation(BsonType.String)]
    public Dictionary<Game, Dictionary<Server, string>>? GameUids { get; set; } = null;

    [BsonElement("last_used_regions")] public Dictionary<Game, Server>? LastUsedRegions { get; set; } = null;

    public UserProfileDto ToDto()
    {
        return new UserProfileDto()
        {
            ProfileId = ProfileId,
            LtUid = LtUid,
            LToken = LToken,
            LastCheckIn = LastCheckIn,
            GameUids = GameUids?.ToDictionary(x => x.Key, x => x.Value.ToDictionary(y => y.Key.ToString(), y => y.Value)) ?? [],
            LastUsedRegions = LastUsedRegions?.ToDictionary(x => x.Key, x => x.Value.ToString()) ?? []
        };
    }
}
