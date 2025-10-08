using Mehrak.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace Mehrak.Domain.Models;

public class UserModel
{
    [BsonId] public ulong Id { get; set; }
    [BsonElement("ts")] public DateTime Timestamp { get; set; }
    [BsonElement("profiles")] public IEnumerable<UserProfile>? Profiles { get; set; } = null;
}

public class UserProfile
{
    [BsonElement("profile_id")] public uint ProfileId { get; set; }

    [BsonElement("ltuid")] public ulong LtUid { get; set; }

    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;

    [BsonElement("last_checkin")] public DateTime? LastCheckIn { get; set; }

    [BsonElement("game_uids")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    [BsonRepresentation(BsonType.String)]
    public Dictionary<GameName, Dictionary<string, string>>? GameUids { get; set; } = null;

    [BsonElement("last_used_regions")] public Dictionary<GameName, Regions>? LastUsedRegions { get; set; } = null;
}
