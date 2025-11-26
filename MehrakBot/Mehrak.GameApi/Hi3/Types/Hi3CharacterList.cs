using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Hi3.Types;

public class Hi3CharacterList
{
    [JsonPropertyName("characters")] public required List<Hi3BasicCharacterInfo> Characters { get; init; }
}

public class Hi3BasicCharacterInfo
{
    [JsonPropertyName("character")] public required Hi3CharacterDetail Character { get; init; }
}

public class Hi3CharacterDetail
{
    [JsonPropertyName("avatar")] public required Hi3Avatar Avatar { get; init; }
    [JsonPropertyName("weapon")] public required Hi3Weapon Weapon { get; init; }
    [JsonPropertyName("stigmatas")] public required List<Hi3Stigmata> Stigmatas { get; init; }
    [JsonPropertyName("costumes")] public required List<Hi3Costume> Costumes { get; init; }
}

public class Hi3Avatar
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("star")] public int Star { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
}

public class Hi3Weapon
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("max_rarity")] public int MaxRarity { get; init; }
    [JsonPropertyName("rarity")] public int Rarity { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Hi3.FileName, Id);
    public IImageData ToImageData() => new ImageData(ToImageName(), Icon);
}

public class Hi3Stigmata
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("max_rarity")] public int MaxRarity { get; init; }
    [JsonPropertyName("rarity")] public int Rarity { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Hi3.FileName, Id);
    public IImageData ToImageData() => new ImageData(ToImageName(), Icon);

    public StigmataPosition GetStigmataPosition()
    {
        if (Name.EndsWith("(T)")) return StigmataPosition.T;
        if (Name.EndsWith("(M)")) return StigmataPosition.M;

        return StigmataPosition.B;
    }
}

public class Hi3Costume
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Hi3.CostumeName, Id);

    public IImageData ToImageData() => new ImageData(ToImageName(), Icon);
}

public enum StigmataPosition
{
    T,
    M,
    B
}
