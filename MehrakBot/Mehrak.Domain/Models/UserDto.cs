#region

using Mehrak.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

#endregion

namespace Mehrak.Domain.Models;

public class UserDto
{
    [BsonId] public long Id { get; set; }
    [BsonElement("ts")] public DateTime Timestamp { get; set; }
    [BsonElement("profiles")] public IEnumerable<UserProfileDto>? Profiles { get; set; } = null;
}

public class UserProfileDto
{
    [BsonElement("profile_id")] public int ProfileId { get; set; }

    [BsonElement("ltuid")] public long LtUid { get; set; }

    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;

    [BsonElement("last_checkin")] public DateTime? LastCheckIn { get; set; }

    [BsonElement("game_uids")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    [BsonRepresentation(BsonType.String)]
    public Dictionary<Game, Dictionary<string, string>>? GameUids { get; set; } = null;

    [BsonElement("last_used_regions")] public Dictionary<Game, string>? LastUsedRegions { get; set; } = null;
}
