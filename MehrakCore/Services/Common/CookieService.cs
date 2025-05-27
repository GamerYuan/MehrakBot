#region

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Common;

public class CookieService
{
    private readonly ILogger<CookieService> m_Logger;

    public CookieService(ILogger<CookieService> logger)
    {
        m_Logger = logger;
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
            m_Logger.LogDebug("Starting cookie encryption");

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] cookieBytes = Encoding.UTF8.GetBytes(cookie);
            byte[] encryptedCookie = new byte[cookieBytes.Length];
            byte[] tag = new byte[TagSizeBytes];

            m_Logger.LogTrace("Generated encryption salt and nonce");

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt,
                Pbkdf2Iterations, Pbkdf2HashAlgorithm, KeySizeBytes);

            using (var aesGcm = new AesGcm(key, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, cookieBytes, encryptedCookie, tag);
            }

            m_Logger.LogTrace("Encryption completed, preparing result");

            byte[] combinedCiphertext = new byte[encryptedCookie.Length + tag.Length];
            Buffer.BlockCopy(encryptedCookie, 0, combinedCiphertext, 0, encryptedCookie.Length);
            Buffer.BlockCopy(tag, 0, combinedCiphertext, encryptedCookie.Length, tag.Length);

            var payload = salt.Concat(nonce).Concat(combinedCiphertext).ToArray();
            string combinedDataBase64 = Convert.ToBase64String(payload);

            // Clear sensitive bytes from memory
            Array.Clear(key, 0, key.Length);
            Array.Clear(cookieBytes, 0, cookieBytes.Length);

            m_Logger.LogDebug("Cookie encryption completed successfully");
            return combinedDataBase64;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Error during cookie encryption");
            throw;
        }
    }

    public string DecryptCookie(string encryptedCookie, string passphrase)
    {
        try
        {
            m_Logger.LogDebug("Starting cookie decryption");

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(encryptedCookie);
                m_Logger.LogTrace("Successfully decoded Base64 encrypted cookie data");
            }
            catch (FormatException ex)
            {
                m_Logger.LogWarning(ex, "Invalid Base64 format in encrypted cookie");
                throw;
            }

            byte[] salt = new byte[SaltSizeBytes];
            byte[] nonce = new byte[NonceSizeBytes];

            if (payload.Length < MinCombinedDataLengthBytes)
            {
                m_Logger.LogWarning(
                    "Decryption failed: payload too short ({ActualLength} bytes, expected at least {MinLength} bytes)",
                    payload.Length, MinCombinedDataLengthBytes);
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
                m_Logger.LogWarning("Decryption failed: invalid data format (ciphertext with tag too short)");
                return string.Empty;
            }

            m_Logger.LogTrace("Extracted components from encrypted data for decryption");

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

            try
            {
                using (var aesGcm = new AesGcm(key, TagSizeBytes))
                {
                    aesGcm.Decrypt(nonce, ciphertext, tag, decryptedBytes);
                }

                m_Logger.LogTrace("AES-GCM decryption successful");
            }
            catch (AuthenticationTagMismatchException ex)
            {
                m_Logger.LogWarning(ex, "Authentication tag mismatch during decryption - likely wrong passphrase");
                Array.Clear(key, 0, key.Length);
                throw;
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Unexpected error during AES-GCM decryption");
                Array.Clear(key, 0, key.Length);
                throw;
            }

            string plainTextCookie = Encoding.UTF8.GetString(decryptedBytes);

            // Clear sensitive bytes from memory
            Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
            Array.Clear(key, 0, key.Length);

            m_Logger.LogDebug("Cookie decryption completed successfully");
            return plainTextCookie;
        }
        catch (Exception ex) when (!(ex is AuthenticationTagMismatchException || ex is FormatException))
        {
            m_Logger.LogError(ex, "Error during cookie decryption");
            throw;
        }
    }
}
