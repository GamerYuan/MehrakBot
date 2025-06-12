#region

using System.Security.Cryptography;
using System.Text;

#endregion

namespace MehrakCore.Utility;

public static class DSGenerator
{
    private const string Salt = "6s25p5ox5y14umn1p61aqyyvbvvl3lrt";
    private const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly Random Random = new();

    public static string GenerateDS()
    {
        // Get current Unix timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Generate 6 random characters
        var randomStr = new StringBuilder();
        for (int i = 0; i < 6; i++) randomStr.Append(Characters[Random.Next(Characters.Length)]);

        // Create hash input
        var hashInput = $"salt={Salt}&t={timestamp}&r={randomStr}";

        // Generate MD5 hash
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hashHex = new StringBuilder();

        foreach (var b in hashBytes) hashHex.Append(b.ToString("x2"));

        return $"{timestamp},{randomStr},{hashHex}";
    }
}