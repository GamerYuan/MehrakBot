#region

using System.Security.Cryptography;
using System.Text;

#endregion

namespace Mehrak.GameApi.Utilities;

public static class DSGenerator
{
    private const string Salt = "6s25p5ox5y14umn1p61aqyyvbvvl3lrt";
    private const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly Random Random = new();

    public static string GenerateDS()
    {
        // Get current Unix timestamp
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Generate 6 random characters
        StringBuilder randomStr = new();
        for (int i = 0; i < 6; i++) randomStr.Append(Characters[Random.Next(Characters.Length)]);

        // Create hash input
        string hashInput = $"salt={Salt}&t={timestamp}&r={randomStr}";

        // Generate MD5 hash
        byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(hashInput));
        StringBuilder hashHex = new();

        foreach (byte b in hashBytes) hashHex.Append(b.ToString("x2"));

        return $"{timestamp},{randomStr},{hashHex}";
    }
}