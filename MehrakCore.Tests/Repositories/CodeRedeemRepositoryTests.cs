#region

using MehrakCore.Models;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;

#endregion

namespace MehrakCore.Tests.Repositories;

[Parallelizable(ParallelScope.Fixtures)]
public class CodeRedeemRepositoryTests
{
    private CodeRedeemRepository m_Repository;
    private Mock<ILogger<CodeRedeemRepository>> m_LoggerMock;

    [SetUp]
    public void Setup()
    {
        m_LoggerMock = new Mock<ILogger<CodeRedeemRepository>>();
        m_Repository = new CodeRedeemRepository(MongoTestHelper.Instance.MongoDbService, m_LoggerMock.Object);
    }

    [TearDown]
    public async Task TearDown()
    {
        // Clean up test data after each test
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.DeleteManyAsync(Builders<CodeRedeemModel>.Filter.Empty);
    }

    #region GetCodesAsync Tests

    [Test]
    public async Task GetCodesAsync_WhenNoCodesExist_ShouldReturnEmptyList()
    {
        // Act
        var result = await m_Repository.GetCodesAsync(GameName.Genshin);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetCodesAsync_WhenCodesExist_ShouldReturnCodes()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var expectedCodes = new List<string> { "CODE123", "CODE456", "CODE789" };

        // Insert test data directly
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = expectedCodes
        });

        // Act
        var result = await m_Repository.GetCodesAsync(gameName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(expectedCodes));
    }

    [Test]
    public async Task GetCodesAsync_WhenEmptyCodesListExists_ShouldReturnEmptyList()
    {
        // Arrange
        var gameName = GameName.ZenlessZoneZero;

        // Insert test data with empty codes list
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = new List<string>()
        });

        // Act
        var result = await m_Repository.GetCodesAsync(gameName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetCodesAsync_WithDifferentGames_ShouldReturnCorrectCodes()
    {
        // Arrange
        var genshinCodes = new List<string> { "GENSHIN123" };
        var hsrCodes = new List<string> { "HSR456", "HSR789" };

        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = GameName.Genshin,
            Codes = genshinCodes
        });
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = GameName.HonkaiStarRail,
            Codes = hsrCodes
        });

        // Act
        var genshinResult = await m_Repository.GetCodesAsync(GameName.Genshin);
        var hsrResult = await m_Repository.GetCodesAsync(GameName.HonkaiStarRail);

        // Assert
        Assert.That(genshinResult, Is.EqualTo(genshinCodes));
        Assert.That(hsrResult, Is.EqualTo(hsrCodes));
    }

    #endregion

    #region AddCodesAsync Tests

    [Test]
    public async Task AddCodesAsync_WhenNoEntryExists_ShouldCreateNewEntry()
    {
        // Arrange
        var gameName = GameName.Genshin;
        var codes = new Dictionary<string, CodeStatus>
        {
            { "NEWCODE123", CodeStatus.Valid },
            { "NEWCODE456", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("NEWCODE123"));
        Assert.That(result, Contains.Item("NEWCODE456"));
    }

    [Test]
    public async Task AddCodesAsync_WhenEntryExists_ShouldAddNewValidCodes()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var existingCodes = new List<string> { "EXISTING123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var newCodes = new Dictionary<string, CodeStatus>
        {
            { "NEWCODE789", CodeStatus.Valid },
            { "NEWCODE999", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, newCodes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Contains.Item("EXISTING123"));
        Assert.That(result, Contains.Item("NEWCODE789"));
        Assert.That(result, Contains.Item("NEWCODE999"));
    }

    [Test]
    public async Task AddCodesAsync_WhenAddingDuplicateCodes_ShouldNotAddDuplicates()
    {
        // Arrange
        var gameName = GameName.Genshin;
        var existingCodes = new List<string> { "DUPLICATE123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "DUPLICATE123", CodeStatus.Valid }, // Duplicate
            { "NEWCODE456", CodeStatus.Valid }     // New
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Count(c => c == "DUPLICATE123"), Is.EqualTo(1)); // Should appear only once
        Assert.That(result, Contains.Item("NEWCODE456"));
    }

    [Test]
    public async Task AddCodesAsync_WhenAddingDuplicateCodesCaseInsensitive_ShouldNotAddDuplicates()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var existingCodes = new List<string> { "testcode123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "TESTCODE123", CodeStatus.Valid }, // Same code but different case
            { "NEWCODE456", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("testcode123"));
        Assert.That(result, Contains.Item("NEWCODE456"));
    }

    [Test]
    public async Task AddCodesAsync_WhenRemovingExpiredCodes_ShouldRemoveExpiredCodes()
    {
        // Arrange
        var gameName = GameName.ZenlessZoneZero;
        var existingCodes = new List<string> { "EXPIREDCODE123", "VALIDCODE456", "EXPIREDCODE789" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "EXPIREDCODE123", CodeStatus.Invalid },
            { "EXPIREDCODE789", CodeStatus.Invalid },
            { "NEWVALIDCODE999", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("VALIDCODE456"));
        Assert.That(result, Contains.Item("NEWVALIDCODE999"));
        Assert.That(result, Does.Not.Contain("EXPIREDCODE123"));
        Assert.That(result, Does.Not.Contain("EXPIREDCODE789"));
    }

    [Test]
    public async Task AddCodesAsync_WhenRemovingExpiredCodesCaseInsensitive_ShouldRemoveCorrectly()
    {
        // Arrange
        var gameName = GameName.HonkaiImpact3;
        var existingCodes = new List<string> { "expiredcode123", "VALIDCODE456" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "EXPIREDCODE123", CodeStatus.Invalid } // Different case
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("VALIDCODE456"));
        Assert.That(result, Does.Not.Contain("expiredcode123"));
        Assert.That(result, Does.Not.Contain("EXPIREDCODE123"));
    }

    [Test]
    public async Task AddCodesAsync_WithMixedValidAndExpiredCodes_ShouldHandleCorrectly()
    {
        // Arrange
        var gameName = GameName.Genshin;
        var existingCodes = new List<string> { "KEEPTHIS123", "REMOVETHIS456", "KEEPTHIS789" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "REMOVETHIS456", CodeStatus.Invalid },
            { "NEWVALID999", CodeStatus.Valid },
            { "NEWEXPIRED111", CodeStatus.Invalid }, // This shouldn't be added since it's expired
            { "ANOTHERNEW222", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(4));
        Assert.That(result, Contains.Item("KEEPTHIS123"));
        Assert.That(result, Contains.Item("KEEPTHIS789"));
        Assert.That(result, Contains.Item("NEWVALID999"));
        Assert.That(result, Contains.Item("ANOTHERNEW222"));
        Assert.That(result, Does.Not.Contain("REMOVETHIS456"));
        Assert.That(result, Does.Not.Contain("NEWEXPIRED111"));
    }

    [Test]
    public async Task AddCodesAsync_WithEmptyCodesDictionary_ShouldMakeNoChanges()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var existingCodes = new List<string> { "EXISTING123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>();

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("EXISTING123"));
    }

    [Test]
    public async Task AddCodesAsync_WithOnlyExpiredCodesNoneToRemove_ShouldMakeNoChanges()
    {
        // Arrange
        var gameName = GameName.ZenlessZoneZero;
        var existingCodes = new List<string> { "EXISTING123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "NONEXISTENTEXPIRED456", CodeStatus.Invalid } // Code doesn't exist, so nothing to remove
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("EXISTING123"));
    }

    [Test]
    public async Task AddCodesAsync_WithOnlyValidCodesAlreadyExisting_ShouldMakeNoChanges()
    {
        // Arrange
        var gameName = GameName.Genshin;
        var existingCodes = new List<string> { "EXISTING123", "EXISTING456" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "EXISTING123", CodeStatus.Valid },
            { "EXISTING456", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Contains.Item("EXISTING123"));
        Assert.That(result, Contains.Item("EXISTING456"));
    }

    [Test]
    public async Task AddCodesAsync_ComplexScenario_ShouldHandleAllOperationsCorrectly()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var existingCodes = new List<string>
        {
            "KEEP123",
            "REMOVE456",
            "keep789",      // lowercase version
            "REMOVE999"     // to be removed
        };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "REMOVE456", CodeStatus.Invalid },        // Remove this
            { "remove999", CodeStatus.Invalid },        // Remove this (case insensitive)
            { "KEEP123", CodeStatus.Valid },            // Already exists, no change
            { "KEEP789", CodeStatus.Valid },            // Already exists (case insensitive), no change
            { "NEWNEW111", CodeStatus.Valid },          // Add this new one
            { "NEWNEW222", CodeStatus.Valid },          // Add this new one
            { "EXPIREDNEW333", CodeStatus.Invalid }     // Don't add this expired one
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(4));

        // Should keep these
        Assert.That(result, Contains.Item("KEEP123"));
        Assert.That(result, Contains.Item("keep789"));

        // Should add these new ones
        Assert.That(result, Contains.Item("NEWNEW111"));
        Assert.That(result, Contains.Item("NEWNEW222"));

        // Should remove these
        Assert.That(result, Does.Not.Contain("REMOVE456"));
        Assert.That(result, Does.Not.Contain("REMOVE999"));

        // Should not add expired new codes
        Assert.That(result, Does.Not.Contain("EXPIREDNEW333"));
    }

    #endregion

    #region Logging Tests

    [Test]
    public async Task GetCodesAsync_ShouldLogDebugMessage()
    {
        // Arrange
        var gameName = GameName.Genshin;

        // Act
        await m_Repository.GetCodesAsync(gameName);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Fetching codes for game: {gameName}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task AddCodesAsync_WhenNoChanges_ShouldLogDebugMessage()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var existingCodes = new List<string> { "EXISTING123" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "EXISTING123", CodeStatus.Valid } // Already exists
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"No changes to codes for game: {gameName}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task AddCodesAsync_WhenChangesOccur_ShouldLogInformationMessage()
    {
        // Arrange
        var gameName = GameName.ZenlessZoneZero;
        var existingCodes = new List<string> { "EXISTING123", "TOREMOVE456" };

        // Create existing entry
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        await collection.InsertOneAsync(new CodeRedeemModel
        {
            Game = gameName,
            Codes = existingCodes
        });

        var codes = new Dictionary<string, CodeStatus>
        {
            { "TOREMOVE456", CodeStatus.Invalid },
            { "NEWCODE789", CodeStatus.Valid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        m_LoggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Added 1 new codes, removed 1 expired codes for game")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Test]
    public async Task AddCodesAsync_WhenAllCodesAreExpiredAndDontExist_ShouldMakeNoChanges()
    {
        // Arrange
        var gameName = GameName.HonkaiImpact3;
        var codes = new Dictionary<string, CodeStatus>
        {
            { "NONEXISTENT1", CodeStatus.Invalid },
            { "NONEXISTENT2", CodeStatus.Invalid }
        };

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty); // Entry is created but no codes are added
    }

    [Test]
    public async Task AddCodesAsync_WhenEntryExistsButCodesListIsNull_ShouldHandleGracefully()
    {
        // Arrange
        var gameName = GameName.Genshin;

        // Create entry with null codes list (shouldn't happen in practice, but test robustness)
        var collection = MongoTestHelper.Instance.MongoDbService.Codes;
        var entry = new CodeRedeemModel
        {
            Game = gameName,
            Codes = null!
        };
        await collection.InsertOneAsync(entry);

        var codes = new Dictionary<string, CodeStatus>
        {
            { "NEWCODE123", CodeStatus.Valid }
        };

        // Act & Assert
        // This should not throw an exception
        await m_Repository.AddCodesAsync(gameName, codes);

        // The entry should be updated with new codes
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Contains.Item("NEWCODE123"));
    }

    [Test]
    public async Task AddCodesAsync_WithVeryLargeCodesList_ShouldHandleCorrectly()
    {
        // Arrange
        var gameName = GameName.HonkaiStarRail;
        var codes = new Dictionary<string, CodeStatus>();

        // Add 1000 codes
        for (int i = 0; i < 1000; i++)
        {
            codes.Add($"CODE{i:D4}", CodeStatus.Valid);
        }

        // Act
        await m_Repository.AddCodesAsync(gameName, codes);

        // Assert
        var result = await m_Repository.GetCodesAsync(gameName);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1000));
        Assert.That(result, Contains.Item("CODE0000"));
        Assert.That(result, Contains.Item("CODE0999"));
    }

    #endregion
}
