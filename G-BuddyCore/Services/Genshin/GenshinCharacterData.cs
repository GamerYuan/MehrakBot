namespace G_BuddyCore.Services.Genshin;

public abstract class GenshinItemBase
{
    public uint Id { get; set; }
    public required string Name { get; set; }
    public uint Level { get; set; }
    public uint Rarity { get; set; }
    public required string Icon { get; set; }
    public required string Image { get; set; }
}

public class GenshinCharacterListData : GenshinItemBase
{
    public string Element { get; set; }
    public uint ActivatedConstellationNum { get; set; }
    public uint WeaponType { get; set; }
}

public class GenshinCharacterDetailData : GenshinItemBase
{
}

public class GenshinWeaponData : GenshinItemBase
{
    public uint Type { get; set; }
    public uint AffixLevel { get; set; }
}
