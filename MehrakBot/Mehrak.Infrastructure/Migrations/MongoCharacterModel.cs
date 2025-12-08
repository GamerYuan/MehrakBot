#region

using Mehrak.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

#endregion

namespace Mehrak.Domain.Models;

[Obsolete]
public class MongoCharacterModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("game")] public Game Game { get; set; }

    [BsonElement("characters")] public required List<string> Characters { get; set; }
}
