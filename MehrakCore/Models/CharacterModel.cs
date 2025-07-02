#region

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

#endregion

namespace MehrakCore.Models;

public class CharacterModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("game")]
    public GameName Game { get; set; }

    [BsonElement("characters")]
    public required List<string> Characters { get; set; }
}
