using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Models;

public class CodeRedeemModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("game")]
    public GameName Game { get; set; }

    [BsonElement("codes")]
    public List<string>? Codes { get; set; }
}
