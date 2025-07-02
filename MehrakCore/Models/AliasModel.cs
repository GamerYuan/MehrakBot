#region

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

#endregion

namespace MehrakCore.Models;

public class AliasModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("game")] public GameName Game { get; set; }

    [BsonElement("alias")] public required Dictionary<string, string> Alias { get; set; }
}
