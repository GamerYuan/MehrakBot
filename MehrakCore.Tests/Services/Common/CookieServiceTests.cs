#region

using System.Security.Cryptography;
using System.Text;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures | ParallelScope.Children)]
public class CookieServiceTests
{
    private string m_Cookie;
    private string m_Passphrase;
    private CookieService m_CookieService;

    [SetUp]
    public void Setup()
    {
        m_Cookie = "test_cookie";
        m_Passphrase = "test_passphrase";
        m_CookieService = new CookieService(NullLogger<CookieService>.Instance);
    }

    [Test]
    public void TestEncryptionDecryption_ReturnsSameCookieString()
    {
        var encrypted = m_CookieService.EncryptCookie(m_Cookie, m_Passphrase);
        var decrypted = m_CookieService.DecryptCookie(encrypted, m_Passphrase);
        Assert.That(decrypted, Is.EqualTo(m_Cookie));
    }

    [Test]
    public void EncryptCookie_WithSameInputs_ProducesDifferentOutputs()
    {
        var encrypted1 = m_CookieService.EncryptCookie(m_Cookie, m_Passphrase);
        var encrypted2 = m_CookieService.EncryptCookie(m_Cookie, m_Passphrase);

        Assert.That(encrypted1, Is.Not.EqualTo(encrypted2),
            "Encryption should use different salt/nonce each time");
    }

    [Test]
    public void DecryptCookie_WithWrongPassphrase_ThrowsException()
    {
        var encrypted = m_CookieService.EncryptCookie(m_Cookie, m_Passphrase);

        // Should throw cryptographic exception or return empty string
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_CookieService.DecryptCookie(encrypted, "wrong_passphrase"));
    }

    [Test]
    public void DecryptCookie_WithTamperedData_ThrowsException()
    {
        var encrypted = m_CookieService.EncryptCookie(m_Cookie, m_Passphrase);

        // Tamper with the encrypted data
        var bytes = Convert.FromBase64String(encrypted);
        if (bytes.Length > 20) bytes[20] ^= 0xFF; // Flip bits in part of the ciphertext
        var tampered = Convert.ToBase64String(bytes);

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_CookieService.DecryptCookie(tampered, m_Passphrase));
    }

    [Test]
    public void DecryptCookie_WithInvalidBase64_ReturnsEmptyString()
    {
        Assert.Throws<FormatException>(() =>
            m_CookieService.DecryptCookie("not-valid-base64!", m_Passphrase));
    }

    [Test]
    public void DecryptCookie_WithTooShortData_ReturnsEmptyString()
    {
        // Generate data that's too short
        var shortData = Convert.ToBase64String(Encoding.UTF8.GetBytes("tooshort"));

        var result = m_CookieService.DecryptCookie(shortData, m_Passphrase);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void EncryptDecrypt_WithEmptyString_WorksCorrectly()
    {
        var emptyString = "";
        var encrypted = m_CookieService.EncryptCookie(emptyString, m_Passphrase);
        var decrypted = m_CookieService.DecryptCookie(encrypted, m_Passphrase);

        Assert.That(decrypted, Is.EqualTo(emptyString));
    }

    [Test]
    public void EncryptDecrypt_WithLongString_WorksCorrectly()
    {
        var longString = new string('A', 10000); // 10KB string
        var encrypted = m_CookieService.EncryptCookie(longString, m_Passphrase);
        var decrypted = m_CookieService.DecryptCookie(encrypted, m_Passphrase);

        Assert.That(decrypted, Is.EqualTo(longString));
    }

    [Test]
    public void EncryptDecrypt_WithSpecialCharacters_WorksCorrectly()
    {
        var specialChars = "!@#$%^&*()_+-=[]{}|;':\",./<>?éñçßÜØÆÑ😀";
        var encrypted = m_CookieService.EncryptCookie(specialChars, m_Passphrase);
        var decrypted = m_CookieService.DecryptCookie(encrypted, m_Passphrase);

        Assert.That(decrypted, Is.EqualTo(specialChars));
    }
}