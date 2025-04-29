#region

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

#endregion

namespace G_BuddyCore.Services;

public class CookieService
{
    private readonly ILogger SLogger;

    public CookieService()
    {
        // Create a logger factory and logger for this static class
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole());
        SLogger = loggerFactory.CreateLogger<CookieService>();
    }

    // --- Configuration Constants ---
    // Key size for AES-256
    private const int KeySizeBits = 256;

    private const int KeySizeBytes = KeySizeBits / 8;

    // Nonce size for AES-GCM (12 bytes is standard)
    private const int NonceSizeBytes = 12;

    // Tag size for AES-GCM (16 bytes is standard)
    private const int TagSizeBytes = 16;

    // Salt size for PBKDF2 (16 bytes is good)
    private const int SaltSizeBytes = 16;

    // Iteration count for PBKDF2 (Higher is more secure but slower. 100,000+ is a good starting point)
    private const int Pbkdf2Iterations = 150000;

    // Hash algorithm for PBKDF2
    private static readonly HashAlgorithmName Pbkdf2HashAlgorithm = HashAlgorithmName.SHA256;

    // --- Calculated constant for validation ---
    private const int MinCombinedDataLengthBytes = SaltSizeBytes + NonceSizeBytes + TagSizeBytes;

    public string EncryptCookie(string cookie, string passphrase)
    {
        try
        {
            SLogger.LogDebug("Starting cookie encryption");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] cookieBytes = Encoding.UTF8.GetBytes(cookie);
            byte[] encryptedCookie = new byte[cookieBytes.Length];
            byte[] tag = new byte[TagSizeBytes];

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt,
                Pbkdf2Iterations, Pbkdf2HashAlgorithm, KeySizeBytes);

            using (var aesGcm = new AesGcm(key, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, cookieBytes, encryptedCookie, tag);
            }

            byte[] combinedCiphertext = new byte[encryptedCookie.Length + tag.Length];
            Buffer.BlockCopy(encryptedCookie, 0, combinedCiphertext, 0, encryptedCookie.Length);
            Buffer.BlockCopy(tag, 0, combinedCiphertext, encryptedCookie.Length, tag.Length);

            var payload = salt.Concat(nonce).Concat(combinedCiphertext).ToArray();
            string combinedDataBase64 = Convert.ToBase64String(payload);

            // Clear sensitive bytes from memory
            Array.Clear(key, 0, key.Length);
            Array.Clear(cookieBytes, 0, cookieBytes.Length);

            SLogger.LogDebug("Cookie encryption completed successfully");
            return combinedDataBase64;
        }
        catch (Exception ex)
        {
            SLogger.LogError(ex, "Error during cookie encryption");
            throw;
        }
    }

    public string DecryptCookie(string encryptedCookie, string passphrase)
    {
        try
        {
            SLogger.LogDebug("Starting cookie decryption");

            byte[] payload = Convert.FromBase64String(encryptedCookie);
            byte[] salt = new byte[SaltSizeBytes];
            byte[] nonce = new byte[NonceSizeBytes];

            if (payload.Length < MinCombinedDataLengthBytes)
            {
                SLogger.LogWarning("Decryption failed: payload too short");
                return string.Empty;
            }

            Buffer.BlockCopy(payload, 0, salt, 0, SaltSizeBytes);
            Buffer.BlockCopy(payload, SaltSizeBytes, nonce, 0, NonceSizeBytes);

            int combinedCiphertextLength = payload.Length - SaltSizeBytes - NonceSizeBytes;
            byte[] combinedCiphertextWithTag = new byte[combinedCiphertextLength];
            Buffer.BlockCopy(payload, SaltSizeBytes + NonceSizeBytes, combinedCiphertextWithTag, 0,
                combinedCiphertextLength);

            // --- VALIDATE AND SPLIT Combined Ciphertext + Tag ---
            if (combinedCiphertextWithTag.Length < TagSizeBytes)
            {
                SLogger.LogWarning("Decryption failed: invalid data format");
                return string.Empty;
            }

            // Extract Tag (last TagSizeBytes bytes of the combinedCiphertextWithTag)
            byte[] tag = new byte[TagSizeBytes];
            Buffer.BlockCopy(combinedCiphertextWithTag, combinedCiphertextWithTag.Length - TagSizeBytes, tag, 0,
                TagSizeBytes);

            // Extract Ciphertext (everything before the tag in combinedCiphertextWithTag)
            int ciphertextLength = combinedCiphertextWithTag.Length - TagSizeBytes;
            byte[] ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(combinedCiphertextWithTag, 0, ciphertext, 0, ciphertextLength);


            // Now we have salt, nonce, ciphertext, and tag. Proceed with decryption.
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt,
                Pbkdf2Iterations,
                Pbkdf2HashAlgorithm,
                KeySizeBytes);

            byte[] decryptedBytes = new byte[ciphertext.Length];

            using (var aesGcm = new AesGcm(key, TagSizeBytes)) // Pass tag size if required by .NET version
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, decryptedBytes);
            }

            string plainTextCookie = Encoding.UTF8.GetString(decryptedBytes);

            // Clear sensitive bytes from memory
            Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
            Array.Clear(key, 0, key.Length);

            SLogger.LogDebug("Cookie decryption completed successfully");
            return plainTextCookie;
        }
        catch (Exception ex)
        {
            SLogger.LogError(ex, "Error during cookie decryption");
            throw;
        }
    }
}
