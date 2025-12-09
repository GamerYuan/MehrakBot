using System.Security.Cryptography;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Infrastructure.Tests.UnitTests.Services;

/// <summary>
/// Unit tests for CookieEncryptionService validating encryption/decryption operations,
/// error handling, passphrase validation, and edge cases.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CookieEncryptionServiceTests
{
    private Mock<ILogger<CookieEncryptionService>> m_MockLogger;
    private CookieEncryptionService m_EncryptionService;

    [SetUp]
    public void SetUp()
    {
        m_MockLogger = new Mock<ILogger<CookieEncryptionService>>();
        m_EncryptionService = new CookieEncryptionService(m_MockLogger.Object);
    }

    #region Encrypt Tests

    [Test]
    public void Encrypt_WithValidInput_ReturnsBase64String()
    {
        // Arrange
        const string plainText = "test-cookie-data";
        const string passphrase = "strong-passphrase-123";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
        Assert.That(() => Convert.FromBase64String(result), Throws.Nothing);
    }

    [Test]
    public void Encrypt_WithSameInputTwice_ReturnsDifferentResults()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";

        // Act
        var result1 = m_EncryptionService.Encrypt(plainText, passphrase);
        var result2 = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result1, Is.Not.EqualTo(result2), "Results should differ due to random salt and nonce");
    }

    [Test]
    public void Encrypt_WithEmptyPlainText_ReturnsValidEncryptedString()
    {
        // Arrange
        const string plainText = "";
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void Encrypt_WithLongPlainText_ReturnsValidEncryptedString()
    {
        // Arrange
        var plainText = new string('a', 10000);
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
        Assert.That(() => Convert.FromBase64String(result), Throws.Nothing);
    }

    [Test]
    public void Encrypt_WithSpecialCharacters_ReturnsValidEncryptedString()
    {
        // Arrange
        const string plainText = "Test™©®€£¥§¶†‡•…‰′″‹›™";
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void Encrypt_WithUnicodeCharacters_ReturnsValidEncryptedString()
    {
        // Arrange
        const string plainText = "Hello 世界 مرحبا мир";
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void Encrypt_WithEmptyPassphrase_ReturnsValidEncryptedString()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "";

        // Act
        var result = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void Encrypt_LogsDebugMessages()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";

        // Act
        m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Starting cookie encryption")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("completed successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Decrypt Tests

    [Test]
    public void Decrypt_WithValidEncryptedData_ReturnsOriginalPlainText()
    {
        // Arrange
        const string plainText = "test-cookie-data";
        const string passphrase = "strong-passphrase-123";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Decrypt_WithEmptyPlainText_ReturnsEmptyString()
    {
        // Arrange
        const string plainText = "";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Decrypt_WithLongPlainText_ReturnsOriginalText()
    {
        // Arrange
        var plainText = new string('a', 10000);
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Decrypt_WithSpecialCharacters_ReturnsOriginalText()
    {
        // Arrange
        const string plainText = "Test™©®€£¥§¶†‡•…‰′″‹›™";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Decrypt_WithUnicodeCharacters_ReturnsOriginalText()
    {
        // Arrange
        const string plainText = "Hello 世界 مرحبا мир";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Decrypt_WithWrongPassphrase_ThrowsAuthenticationTagMismatchException()
    {
        // Arrange
        const string plainText = "test-data";
        const string correctPassphrase = "correct-passphrase";
        const string wrongPassphrase = "wrong-passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, correctPassphrase);

        // Act & Assert
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_EncryptionService.Decrypt(encrypted, wrongPassphrase));
    }

    [Test]
    public void Decrypt_WithInvalidBase64_ThrowsFormatException()
    {
        // Arrange
        const string invalidBase64 = "This is not valid base64!!!";
        const string passphrase = "passphrase";

        // Act & Assert
        Assert.Throws<FormatException>(() =>
  m_EncryptionService.Decrypt(invalidBase64, passphrase));
    }

    [Test]
    public void Decrypt_WithTooShortPayload_ReturnsEmptyString()
    {
        // Arrange
        // Create a payload shorter than minimum required (salt + nonce + tag = 44 bytes)
        var shortPayload = Convert.ToBase64String(new byte[20]);
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Decrypt(shortPayload, passphrase);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Decrypt_WithCorruptedData_ThrowsException()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Corrupt the encrypted data
        var bytes = Convert.FromBase64String(encrypted);
        bytes[^1] ^= 0xFF; // Flip bits in the tag
        var corrupted = Convert.ToBase64String(bytes);

        // Act & Assert
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_EncryptionService.Decrypt(corrupted, passphrase));
    }

    [Test]
    public void Decrypt_WithEmptyString_ReturnsEmptyString()
    {
        // Arrange
        const string emptyString = "";
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Decrypt(emptyString, passphrase);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Decrypt_LogsDebugMessages()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Reset logger to clear previous calls
        m_MockLogger.Invocations.Clear();

        // Act
        m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Starting cookie decryption")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("completed successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void Decrypt_WithWrongPassphrase_LogsWarning()
    {
        // Arrange
        const string plainText = "test-data";
        const string correctPassphrase = "correct";
        const string wrongPassphrase = "wrong";
        var encrypted = m_EncryptionService.Encrypt(plainText, correctPassphrase);

        m_MockLogger.Invocations.Clear();

        // Act & Assert
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_EncryptionService.Decrypt(encrypted, wrongPassphrase));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Authentication tag mismatch")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public void Decrypt_WithInvalidBase64_LogsWarning()
    {
        // Arrange
        const string invalidBase64 = "Not valid base64!!!";
        const string passphrase = "passphrase";

        // Act & Assert
        Assert.Throws<FormatException>(() =>
            m_EncryptionService.Decrypt(invalidBase64, passphrase));

        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Invalid Base64 format")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Round-Trip Tests

    [Test]
    public void RoundTrip_WithVariousPassphrases_WorksCorrectly()
    {
        // Arrange
        const string plainText = "test-data";
        var passphrases = new[]
        {
            "short",
            "medium-length-passphrase",
            "very-long-passphrase-with-many-characters-123456789",
            "P@ssw0rd!",
            "123456",
            "пароль",
            "密码"
        };

        foreach (var passphrase in passphrases)
        {
            // Act
            var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);
            var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

            // Assert
            Assert.That(decrypted, Is.EqualTo(plainText),
                          $"Round-trip failed for passphrase: {passphrase}");
        }
    }

    [Test]
    public void RoundTrip_WithVariousPlainTexts_WorksCorrectly()
    {
        // Arrange
        const string passphrase = "test-passphrase";
        var plainTexts = new[]
        {
            "",
            "a",
            "simple text",
            "Text with numbers 123456",
            "Special chars: !@#$%^&*()_+-=[]{}|;:',.<>?/~`",
            "{\"key\":\"value\",\"number\":42}",
            "Line1\nLine2\nLine3",
            "Tab\tseparated\tvalues",
            new string('x', 1000)
        };

        foreach (var plainText in plainTexts)
        {
            // Act
            var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);
            var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

            // Assert
            Assert.That(decrypted, Is.EqualTo(plainText),
                 $"Round-trip failed for plaintext of length {plainText.Length}");
        }
    }

    [Test]
    public void RoundTrip_MultipleEncryptionsWithSameData_AllDecryptCorrectly()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        const int iterations = 10;

        // Act & Assert
        for (var i = 0; i < iterations; i++)
        {
            var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);
            var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

            Assert.That(decrypted, Is.EqualTo(plainText),
                $"Round-trip failed on iteration {i + 1}");
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Encrypt_WithNullPlainText_ThrowsArgumentNullException()
    {
        // Arrange
        const string passphrase = "passphrase";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            m_EncryptionService.Encrypt(null!, passphrase));
    }

    [Test]
    public void Encrypt_WithNullPassphrase_ThrowsArgumentNullException()
    {
        // Arrange
        const string plainText = "test-data";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            m_EncryptionService.Encrypt(plainText, null!));
    }

    [Test]
    public void Decrypt_WithNullCipherText_ThrowsArgumentNullException()
    {
        // Arrange
        const string passphrase = "passphrase";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            m_EncryptionService.Decrypt(null!, passphrase));
    }

    [Test]
    public void Decrypt_WithNullPassphrase_ThrowsArgumentNullException()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            m_EncryptionService.Decrypt(encrypted, null!));
    }

    [Test]
    public void Decrypt_WithPayloadMissingTag_ReturnsEmptyString()
    {
        // Arrange
        // Create a payload with salt + nonce but missing tag (< 44 bytes minimum)
        var bytes = new byte[28]; // Just salt (16) + nonce (12)
        RandomNumberGenerator.Fill(bytes);
        var invalidPayload = Convert.ToBase64String(bytes);
        const string passphrase = "passphrase";

        // Act
        var result = m_EncryptionService.Decrypt(invalidPayload, passphrase);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Encrypt_ProducesMinimumExpectedLength()
    {
        // Arrange
        const string plainText = "a";
        const string passphrase = "passphrase";

        // Act
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);
        var bytes = Convert.FromBase64String(encrypted);

        // Assert
        // Minimum: salt(16) + nonce(12) + ciphertext(1) + tag(16) = 45 bytes
        Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(45));
    }

    [Test]
    public void Decrypt_WithOnlyMinimumBytes_ThrowsAuthenticationTagMismatchException()
    {
        // Arrange
        // Exactly 44 bytes (salt + nonce + tag, no ciphertext)
        var bytes = new byte[44];
        RandomNumberGenerator.Fill(bytes);
        var payload = Convert.ToBase64String(bytes);
        const string passphrase = "passphrase";

        // Act & Assert
        // This will throw because the tag won't match with random data
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            m_EncryptionService.Decrypt(payload, passphrase));
    }

    #endregion

    #region Security Tests

    [Test]
    public void Encrypt_WithSameInputDifferentInstances_ProducesDifferentResults()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        var service1 = new CookieEncryptionService(m_MockLogger.Object);
        var service2 = new CookieEncryptionService(m_MockLogger.Object);

        // Act
        var result1 = service1.Encrypt(plainText, passphrase);
        var result2 = service2.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(result1, Is.Not.EqualTo(result2),
            "Different encryptions should produce different results due to random salt/nonce");
    }

    [Test]
    public void Decrypt_AcrossInstances_WorksCorrectly()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "passphrase";
        var encryptService = new CookieEncryptionService(m_MockLogger.Object);
        var decryptService = new CookieEncryptionService(m_MockLogger.Object);

        // Act
        var encrypted = encryptService.Encrypt(plainText, passphrase);
        var decrypted = decryptService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(plainText));
    }

    [Test]
    public void Encrypt_ResultDoesNotContainPlainText()
    {
        // Arrange
        const string plainText = "very-unique-secret-text-12345";
        const string passphrase = "passphrase";

        // Act
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(encrypted, Does.Not.Contain(plainText),
            "Encrypted result should not contain plaintext");
    }

    [Test]
    public void Encrypt_ResultDoesNotContainPassphrase()
    {
        // Arrange
        const string plainText = "test-data";
        const string passphrase = "very-unique-passphrase-12345";

        // Act
        var encrypted = m_EncryptionService.Encrypt(plainText, passphrase);

        // Assert
        Assert.That(encrypted, Does.Not.Contain(passphrase),
            "Encrypted result should not contain passphrase");
    }

    #endregion

    #region Real-World Scenarios

    [Test]
    public void RoundTrip_WithCookieData_WorksCorrectly()
    {
        // Arrange
        const string cookieData = "session_id=abc123; user_id=12345; auth_token=xyz789";
        const string passphrase = "cookie-encryption-key";

        // Act
        var encrypted = m_EncryptionService.Encrypt(cookieData, passphrase);
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(cookieData));
    }

    [Test]
    public void RoundTrip_WithJsonData_WorksCorrectly()
    {
        // Arrange
        const string jsonData = "{\"userId\":\"12345\",\"sessionId\":\"abc-def-ghi\",\"expiresAt\":\"2024-12-31T23:59:59Z\"}";
        const string passphrase = "json-encryption-key";

        // Act
        var encrypted = m_EncryptionService.Encrypt(jsonData, passphrase);
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(jsonData));
    }

    [Test]
    public void RoundTrip_WithBase64Data_WorksCorrectly()
    {
        // Arrange
        const string base64Data = "VGhpcyBpcyBhIHRlc3QgbWVzc2FnZQ==";
        const string passphrase = "passphrase";

        // Act
        var encrypted = m_EncryptionService.Encrypt(base64Data, passphrase);
        var decrypted = m_EncryptionService.Decrypt(encrypted, passphrase);

        // Assert
        Assert.That(decrypted, Is.EqualTo(base64Data));
    }

    #endregion
}
