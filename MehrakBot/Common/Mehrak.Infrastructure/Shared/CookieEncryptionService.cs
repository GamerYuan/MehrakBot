#region

using System.Security.Cryptography;
using System.Text;
using Mehrak.Domain.Shared.Services;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Infrastructure.Shared;

public class CookieEncryptionService : IEncryptionService
{
    private readonly ILogger<CookieEncryptionService> m_Logger;

    public CookieEncryptionService(ILogger<CookieEncryptionService> logger)
    {
        m_Logger = logger;
    }

    private const int KeySizeBits = 256;
    private const int KeySizeBytes = KeySizeBits / 8;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int SaltSizeBytes = 16;
    private const int CurrentPbkdf2Iterations = 600000;
    private const int LegacyPbkdf2Iterations = 150000;
    private const byte PayloadFormatVersion = 0x01;
    private static readonly HashAlgorithmName Pbkdf2HashAlgorithm = HashAlgorithmName.SHA256;
    private const int MinCombinedDataLengthBytes = SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
    private const int VersionPrefixBytes = 1;
    private const int MinVersionedDataLengthBytes = VersionPrefixBytes + MinCombinedDataLengthBytes;

    public string Encrypt(string plainText, string passphrase)
    {
        return EncryptWithParameters(plainText, passphrase, CurrentPbkdf2Iterations, true);
    }

    public bool IsLegacyFormat(string cipherText)
    {
        try
        {
            var payload = Convert.FromBase64String(cipherText);
            return !(payload.Length >= MinVersionedDataLengthBytes && payload[0] == PayloadFormatVersion);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal string EncryptLegacyForTests(string plainText, string passphrase, byte[]? saltOverride = null)
    {
        return EncryptWithParameters(plainText, passphrase, LegacyPbkdf2Iterations, false, saltOverride);
    }

    private string EncryptWithParameters(
        string plainText,
        string passphrase,
        int iterations,
        bool prefixVersion,
        byte[]? saltOverride = null)
    {
        try
        {
            m_Logger.LogDebug("Starting cookie encryption");

            var salt = saltOverride ?? RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var cookieBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedCookie = new byte[cookieBytes.Length];
            var tag = new byte[TagSizeBytes];

            m_Logger.LogTrace("Generated encryption salt and nonce");

            var key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(passphrase),
                salt,
                iterations,
                Pbkdf2HashAlgorithm,
                KeySizeBytes);

            using (AesGcm aesGcm = new(key, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, cookieBytes, encryptedCookie, tag);
            }

            m_Logger.LogTrace("Encryption completed, preparing result");

            var prefixLength = prefixVersion ? VersionPrefixBytes : 0;
            var payload = new byte[prefixLength + SaltSizeBytes + NonceSizeBytes + encryptedCookie.Length + tag.Length];
            var index = 0;

            if (prefixVersion) payload[index++] = PayloadFormatVersion;
            Buffer.BlockCopy(salt, 0, payload, index, SaltSizeBytes);
            index += SaltSizeBytes;
            Buffer.BlockCopy(nonce, 0, payload, index, NonceSizeBytes);
            index += NonceSizeBytes;
            Buffer.BlockCopy(encryptedCookie, 0, payload, index, encryptedCookie.Length);
            index += encryptedCookie.Length;
            Buffer.BlockCopy(tag, 0, payload, index, tag.Length);

            var combinedDataBase64 = Convert.ToBase64String(payload);

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

    public string Decrypt(string cipherText, string passphrase)
    {
        try
        {
            m_Logger.LogDebug("Starting cookie decryption");

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(cipherText);
                m_Logger.LogTrace("Successfully decoded Base64 encrypted cookie data");
            }
            catch (FormatException ex)
            {
                m_Logger.LogWarning(ex, "Invalid Base64 format in encrypted cookie");
                throw new CryptographicException("Decryption failed: invalid Base64 format", ex);
            }

            if (payload.Length >= MinVersionedDataLengthBytes && payload[0] == PayloadFormatVersion)
            {
                try
                {
                    var plainText = DecryptWithParameters(payload, passphrase, CurrentPbkdf2Iterations,
                        VersionPrefixBytes);
                    m_Logger.LogDebug("Cookie decryption completed successfully");
                    return plainText;
                }
                catch (AuthenticationTagMismatchException ex)
                {
                    // ~1/256 of legacy salts start with the version marker byte; retry the
                    // full payload under the legacy interpretation before failing.
                    m_Logger.LogDebug(ex, "Versioned decryption failed tag check, retrying as legacy format");

                    try
                    {
                        var plainText = DecryptWithParameters(payload, passphrase, LegacyPbkdf2Iterations, 0);
                        m_Logger.LogWarning(
                            "Decrypted legacy-format credential whose salt collided with version marker; consider re-saving this profile to upgrade it");
                        m_Logger.LogDebug("Cookie decryption completed successfully");
                        return plainText;
                    }
                    catch (AuthenticationTagMismatchException legacyEx)
                    {
                        m_Logger.LogWarning(legacyEx,
                            "Authentication tag mismatch during decryption - likely wrong passphrase");
                        throw;
                    }
                }
            }

            try
            {
                var plainText = DecryptWithParameters(payload, passphrase, LegacyPbkdf2Iterations, 0);
                m_Logger.LogDebug("Cookie decryption completed successfully");
                return plainText;
            }
            catch (AuthenticationTagMismatchException ex)
            {
                m_Logger.LogWarning(ex,
                    "Authentication tag mismatch during decryption - likely wrong passphrase");
                throw;
            }
        }
        catch (Exception ex) when (ex is not AuthenticationTagMismatchException and not CryptographicException)
        {
            m_Logger.LogError(ex, "Error during cookie decryption");
            throw;
        }
    }

    private string DecryptWithParameters(byte[] payload, string passphrase, int iterations, int offset)
    {
        if (payload.Length - offset < MinCombinedDataLengthBytes)
        {
            m_Logger.LogWarning(
                "Decryption failed: payload too short ({ActualLength} bytes, expected at least {MinLength} bytes)",
                payload.Length,
                MinCombinedDataLengthBytes);
            throw new CryptographicException("Decryption failed: payload too short");
        }

        var salt = new byte[SaltSizeBytes];
        var nonce = new byte[NonceSizeBytes];

        Buffer.BlockCopy(payload, offset, salt, 0, SaltSizeBytes);
        Buffer.BlockCopy(payload, offset + SaltSizeBytes, nonce, 0, NonceSizeBytes);

        var combinedCiphertextLength = payload.Length - offset - SaltSizeBytes - NonceSizeBytes;
        var combinedCiphertextWithTag = new byte[combinedCiphertextLength];
        Buffer.BlockCopy(
            payload,
            offset + SaltSizeBytes + NonceSizeBytes,
            combinedCiphertextWithTag,
            0,
            combinedCiphertextLength);

        if (combinedCiphertextWithTag.Length < TagSizeBytes)
        {
            m_Logger.LogWarning("Decryption failed: invalid data format (ciphertext with tag too short)");
            throw new CryptographicException("Decryption failed: invalid data format (ciphertext with tag too short)");
        }

        var tag = new byte[TagSizeBytes];
        Buffer.BlockCopy(
            combinedCiphertextWithTag,
            combinedCiphertextWithTag.Length - TagSizeBytes,
            tag,
            0,
            TagSizeBytes);

        var ciphertextLength = combinedCiphertextWithTag.Length - TagSizeBytes;
        var ciphertext = new byte[ciphertextLength];
        Buffer.BlockCopy(combinedCiphertextWithTag, 0, ciphertext, 0, ciphertextLength);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations,
            Pbkdf2HashAlgorithm,
            KeySizeBytes);

        var decryptedBytes = new byte[ciphertext.Length];

        try
        {
            using (AesGcm aesGcm = new(key, TagSizeBytes))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, decryptedBytes);
            }

            m_Logger.LogTrace("AES-GCM decryption successful");
        }
        catch (AuthenticationTagMismatchException)
        {
            Array.Clear(key, 0, key.Length);
            throw;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Unexpected error during AES-GCM decryption");
            Array.Clear(key, 0, key.Length);
            throw;
        }

        var plainTextCookie = Encoding.UTF8.GetString(decryptedBytes);

        Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
        Array.Clear(key, 0, key.Length);

        return plainTextCookie;
    }
}
