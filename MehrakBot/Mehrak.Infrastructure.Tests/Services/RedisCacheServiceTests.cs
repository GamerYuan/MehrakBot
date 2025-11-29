using System.Text;
using System.Text.Json;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for RedisCacheService validating cache operations, serialization/deserialization,
/// expiration handling, and error scenarios.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class RedisCacheServiceTests
{
    private Mock<IDistributedCache> m_MockCache;
    private Mock<ILogger<RedisCacheService>> m_MockLogger;
    private RedisCacheService m_CacheService;

    [SetUp]
    public void SetUp()
    {
        m_MockCache = new Mock<IDistributedCache>();
        m_MockLogger = new Mock<ILogger<RedisCacheService>>();
        m_CacheService = new RedisCacheService(m_MockCache.Object, m_MockLogger.Object);
    }

    #region SetAsync Tests

    [Test]
    public async Task SetAsync_WithValidEntry_StoresSerializedValue()
    {
        // Arrange
        var entry = new TestCacheEntry<string>("test-key", "test-value", TimeSpan.FromMinutes(5));
        var expectedJson = JsonSerializer.Serialize("test-value");
        DistributedCacheEntryOptions? capturedOptions = null;
        byte[]? capturedValue = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
        .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
            (key, value, options, token) =>
            {
                capturedOptions = options;
                capturedValue = value;
            })
        .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        m_MockCache.Verify(c => c.SetAsync(
            "test-key",
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.That(capturedOptions, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(capturedOptions!.AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(Encoding.UTF8.GetString(capturedValue!), Is.EqualTo(expectedJson));
        });
    }

    [Test]
    public async Task SetAsync_WithComplexObject_SerializesCorrectly()
    {
        // Arrange
        var testObject = new TestObject { Id = 42, Name = "Test", IsActive = true };
        var entry = new TestCacheEntry<TestObject>("complex-key", testObject, TimeSpan.FromHours(1));
        var expectedJson = JsonSerializer.Serialize(testObject);
        byte[]? capturedValue = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
         .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
     (key, value, options, token) => capturedValue = value)
                 .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        m_MockCache.Verify(c => c.SetAsync(
            "complex-key",
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.That(Encoding.UTF8.GetString(capturedValue!), Is.EqualTo(expectedJson));
    }

    [Test]
    public async Task SetAsync_WithNullValue_SerializesNull()
    {
        // Arrange
        var entry = new TestCacheEntry<string?>("null-key", null, TimeSpan.FromMinutes(1));
        var expectedJson = JsonSerializer.Serialize<string?>(null);
        byte[]? capturedValue = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, options, token) => capturedValue = value)
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        Assert.That(Encoding.UTF8.GetString(capturedValue!), Is.EqualTo(expectedJson));
    }

    [Test]
    public async Task SetAsync_WithShortExpiration_SetsCorrectExpirationTime()
    {
        // Arrange
        var entry = new TestCacheEntry<string>("short-exp-key", "value", TimeSpan.FromSeconds(1));
        DistributedCacheEntryOptions? capturedOptions = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, options, token) => capturedOptions = options)
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        Assert.That(capturedOptions, Is.Not.Null);
        Assert.That(capturedOptions!.AbsoluteExpirationRelativeToNow, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task SetAsync_WithLongExpiration_SetsCorrectExpirationTime()
    {
        // Arrange
        var expiration = TimeSpan.FromDays(365);
        var entry = new TestCacheEntry<string>("long-exp-key", "value", expiration);
        DistributedCacheEntryOptions? capturedOptions = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, options, token) => capturedOptions = options)
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        Assert.That(capturedOptions, Is.Not.Null);
        Assert.That(capturedOptions!.AbsoluteExpirationRelativeToNow, Is.EqualTo(expiration));
    }

    [Test]
    public async Task SetAsync_LogsDebugMessage()
    {
        // Arrange
        var entry = new TestCacheEntry<string>("log-key", "value", TimeSpan.FromMinutes(1));

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("log-key")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion SetAsync Tests

    #region GetAsync Tests

    [Test]
    public async Task GetAsync_WithExistingKey_ReturnsDeserializedValue()
    {
        // Arrange
        const string key = "existing-key";
        const string value = "cached-value";
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.EqualTo(value));
        m_MockCache.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAsync_WithComplexObject_DeserializesCorrectly()
    {
        // Arrange
        const string key = "complex-key";
        var testObject = new TestObject { Id = 99, Name = "Cached", IsActive = false };
        var json = JsonSerializer.Serialize(testObject);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<TestObject>(key);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Id, Is.EqualTo(99));
            Assert.That(result.Name, Is.EqualTo("Cached"));
            Assert.That(result.IsActive, Is.False);
        });
    }

    [Test]
    public async Task GetAsync_WithNonExistingKey_ReturnsDefault()
    {
        // Arrange
        const string key = "non-existing-key";

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        var result = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_WithEmptyString_ReturnsDefault()
    {
        // Arrange
        const string key = "empty-key";
        var bytes = Encoding.UTF8.GetBytes(string.Empty);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_WithValueType_ReturnsDeserializedValue()
    {
        // Arrange
        const string key = "int-key";
        const int value = 42;
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<int>(key);

        // Assert
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public async Task GetAsync_WithNullableValueType_ReturnsValue()
    {
        // Arrange
        const string key = "nullable-key";
        int? value = 100;
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<int?>(key);

        // Assert
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public async Task GetAsync_WithNullableValueTypeNull_ReturnsNull()
    {
        // Arrange
        const string key = "nullable-null-key";
        int? value = null;
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<int?>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAsync_LogsDebugMessage()
    {
        // Arrange
        const string key = "log-get-key";

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        await m_CacheService.GetAsync<string>(key);

        // Assert
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(key)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion GetAsync Tests

    #region RemoveAsync Tests

    [Test]
    public async Task RemoveAsync_WithValidKey_CallsRemove()
    {
        // Arrange
        const string key = "remove-key";

        m_MockCache.Setup(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.RemoveAsync(key);

        // Assert
        m_MockCache.Verify(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RemoveAsync_WithEmptyKey_CallsRemove()
    {
        // Arrange
        const string key = "";

        m_MockCache.Setup(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.RemoveAsync(key);

        // Assert
        m_MockCache.Verify(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RemoveAsync_LogsDebugMessage()
    {
        // Arrange
        const string key = "log-remove-key";

        m_MockCache.Setup(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.RemoveAsync(key);

        // Assert
        m_MockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(key)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion RemoveAsync Tests

    #region Integration Scenarios

    [Test]
    public async Task SetThenGet_WithSameKey_ReturnsCorrectValue()
    {
        // Arrange
        const string key = "integration-key";
        const string value = "integration-value";
        var entry = new TestCacheEntry<string>(key, value, TimeSpan.FromMinutes(5));
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.SetAsync(
            key,
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        await m_CacheService.SetAsync(entry);
        var result = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public async Task SetThenRemoveThenGet_ReturnsDefault()
    {
        // Arrange
        const string key = "temp-key";
        const string value = "temp-value";
        var entry = new TestCacheEntry<string>(key, value, TimeSpan.FromMinutes(1));

        m_MockCache.Setup(c => c.SetAsync(
            key,
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        m_MockCache.Setup(c => c.RemoveAsync(key, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Act
        await m_CacheService.SetAsync(entry);
        await m_CacheService.RemoveAsync(key);
        var result = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.That(result, Is.Null);
    }

    #endregion Integration Scenarios

    #region Edge Cases

    [Test]
    public void GetAsync_WithInvalidJson_ThrowsJsonException()
    {
        // Arrange
        const string key = "invalid-json-key";
        const string invalidJson = "{this is not valid json}";
        var bytes = Encoding.UTF8.GetBytes(invalidJson);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act & Assert
        Assert.ThrowsAsync<JsonException>(async () => await m_CacheService.GetAsync<TestObject>(key));
    }

    [Test]
    public void GetAsync_WithTypeMismatch_ThrowsJsonException()
    {
        // Arrange
        const string key = "type-mismatch-key";
        var stringValue = "not a number";
        var json = JsonSerializer.Serialize(stringValue);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act & Assert
        Assert.ThrowsAsync<JsonException>(async () => await m_CacheService.GetAsync<int>(key));
    }

    [Test]
    public async Task SetAsync_WithSpecialCharactersInKey_StoresCorrectly()
    {
        // Arrange
        const string key = "key:with:colons:and-dashes_underscores";
        const string value = "special-key-value";
        var entry = new TestCacheEntry<string>(key, value, TimeSpan.FromMinutes(1));

        m_MockCache.Setup(c => c.SetAsync(
            key,
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        m_MockCache.Verify(c => c.SetAsync(
            key,
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAsync_CalledMultipleTimes_CachesConsistently()
    {
        // Arrange
        const string key = "consistent-key";
        const string value = "consistent-value";
        var json = JsonSerializer.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result1 = await m_CacheService.GetAsync<string>(key);
        var result2 = await m_CacheService.GetAsync<string>(key);
        var result3 = await m_CacheService.GetAsync<string>(key);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.EqualTo(value));
            Assert.That(result2, Is.EqualTo(value));
            Assert.That(result3, Is.EqualTo(value));
        });

        m_MockCache.Verify(c => c.GetAsync(key, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion Edge Cases

    #region Collection Types

    [Test]
    public async Task SetAsync_WithList_SerializesCorrectly()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3, 4, 5 };
        var entry = new TestCacheEntry<List<int>>("list-key", list, TimeSpan.FromMinutes(1));
        var expectedJson = JsonSerializer.Serialize(list);
        byte[]? capturedValue = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, options, token) => capturedValue = value)
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        Assert.That(Encoding.UTF8.GetString(capturedValue!), Is.EqualTo(expectedJson));
    }

    [Test]
    public async Task GetAsync_WithList_DeserializesCorrectly()
    {
        // Arrange
        const string key = "list-get-key";
        var list = new List<string> { "a", "b", "c" };
        var json = JsonSerializer.Serialize(list);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<List<string>>(key);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Is.EqualTo(new List<string> { "a", "b", "c" }));
    }

    [Test]
    public async Task SetAsync_WithDictionary_SerializesCorrectly()
    {
        // Arrange
        var dict = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };
        var entry = new TestCacheEntry<Dictionary<string, int>>("dict-key", dict, TimeSpan.FromMinutes(1));
        var expectedJson = JsonSerializer.Serialize(dict);
        byte[]? capturedValue = null;

        m_MockCache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, options, token) => capturedValue = value)
            .Returns(Task.CompletedTask);

        // Act
        await m_CacheService.SetAsync(entry);

        // Assert
        Assert.That(Encoding.UTF8.GetString(capturedValue!), Is.EqualTo(expectedJson));
    }

    [Test]
    public async Task GetAsync_WithDictionary_DeserializesCorrectly()
    {
        // Arrange
        const string key = "dict-get-key";
        var dict = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };
        var json = JsonSerializer.Serialize(dict);
        var bytes = Encoding.UTF8.GetBytes(json);

        m_MockCache.Setup(c => c.GetAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await m_CacheService.GetAsync<Dictionary<string, int>>(key);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result!["one"], Is.EqualTo(1));
            Assert.That(result["two"], Is.EqualTo(2));
        });
    }

    #endregion Collection Types

    #region Helper Classes

    private class TestCacheEntry<T> : ICacheEntry<T>
    {
        public TestCacheEntry(string key, T value, TimeSpan expirationTime)
        {
            Key = key;
            Value = value;
            ExpirationTime = expirationTime;
        }

        public string Key { get; }
        public T Value { get; }
        public TimeSpan ExpirationTime { get; }
    }

    private class TestObject
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }

    #endregion Helper Classes
}
