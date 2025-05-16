#region

using MehrakCore.Utility;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

#endregion

namespace MehrakCore.Models;

public enum GameName
{
    Genshin
}

public class UserModel
{
    [BsonId] public ulong Id { get; set; }

    [BsonElement("ltuid")] public ulong LtUid { get; set; }

    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;

    [BsonElement("ts")] public DateTime Timestamp { get; set; }

    [BsonElement("game_uids")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    [BsonRepresentation(BsonType.String)]
    public Dictionary<GameName, Dictionary<string, string>>? GameUids { get; set; } = null;

    [BsonElement("profiles")] public IEnumerable<UserProfile>? Profiles { get; set; } = null;
}

public class UserProfile
{
    [BsonElement("profile_id")] public uint ProfileId { get; set; }

    [BsonElement("ltuid")] public ulong LtUid { get; set; }

    [BsonElement("ltoken")] public string LToken { get; set; } = string.Empty;

    [BsonElement("game_uids")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    [BsonRepresentation(BsonType.String)]
    public Dictionary<GameName, Dictionary<string, string>>? GameUids { get; set; } = null;

    [BsonElement("last_used_regions")] public Dictionary<GameName, Regions>? LastUsedRegions { get; set; } = null;
}
