using Mehrak.Domain.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Mehrak.Infrastructure.Models;

[Obsolete]
public class MongoUserModel
{
    [BsonId] public ulong Id { get; set; }
    [BsonElement("ts")] public DateTime Timestamp { get; set; }
    [BsonElement("profiles")] public IEnumerable<UserProfileDto>? Profiles { get; set; } = null;
}

[Obsolete]
public class MongoUserProfile
{
    [BsonElement("profile_id")] public uint ProfileId { get; set; }
    [BsonElement("ltuid")] public ulong LtUid { get; set; }
    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;
    [BsonElement("last_checkin")] public DateTime? LastCheckIn { get; set; }
}
