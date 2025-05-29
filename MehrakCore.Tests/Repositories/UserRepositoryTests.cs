#region

using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace MehrakCore.Tests.Repositories;

[Parallelizable(ParallelScope.Fixtures)]
public class UserRepositoryTests
{
    private MongoTestHelper m_MongoHelper;
    private UserRepository m_UserRepository;
    private Mock<ILogger<UserRepository>> m_LoggerMock;

    [SetUp]
    public void Setup()
    {
        m_MongoHelper = new MongoTestHelper();
        m_LoggerMock = new Mock<ILogger<UserRepository>>();
        m_UserRepository = new UserRepository(m_MongoHelper.MongoDbService, m_LoggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_MongoHelper.Dispose();
    }

    [Test]
    public async Task GetUserAsync_ShouldReturnNull_WhenUserDoesNotExist()
    {
        // Arrange
        var nonExistentUserId = 12345UL;

        // Act
        var result = await m_UserRepository.GetUserAsync(nonExistentUserId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetUserAsync_ShouldReturnUser_WhenUserExists()
    {
        // Arrange
        var userId = 67890UL;
        var user = new UserModel
        {
            Id = userId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 12345,
                    LToken = "test-token"
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        var result = await m_UserRepository.GetUserAsync(userId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(userId));
        Assert.That(result.Profiles, Is.Not.Null);
        Assert.That(result.Profiles.First().ProfileId, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateOrUpdateUserAsync_ShouldCreateNewUser_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = 13579UL;
        var user = new UserModel
        {
            Id = userId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 54321,
                    LToken = "new-token"
                }
            }
        };

        // Act
        await m_UserRepository.CreateOrUpdateUserAsync(user);
        var createdUser = await m_UserRepository.GetUserAsync(userId);

        // Assert
        Assert.That(createdUser, Is.Not.Null);
        Assert.That(createdUser.Id, Is.EqualTo(userId));
        Assert.That(createdUser.Profiles, Is.Not.Null);
        Assert.That(createdUser.Profiles.First().LtUid, Is.EqualTo(54321));
    }

    [Test]
    public async Task CreateOrUpdateUserAsync_ShouldUpdateExistingUser()
    {
        // Arrange
        var userId = 24680UL;
        var originalUser = new UserModel
        {
            Id = userId,
            Timestamp = DateTime.UtcNow.AddDays(-1),
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 11111,
                    LToken = "original-token"
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(originalUser);

        var updatedUser = new UserModel
        {
            Id = userId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 22222,
                    LToken = "updated-token"
                }
            }
        };

        // Act
        await m_UserRepository.CreateOrUpdateUserAsync(updatedUser);
        var result = await m_UserRepository.GetUserAsync(userId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(userId));
        Assert.That(result.Profiles, Is.Not.Null);
        Assert.That(result.Profiles.First().LtUid, Is.EqualTo(22222));
        Assert.That(result.Profiles.First().LToken, Is.EqualTo("updated-token"));
    }

    [Test]
    public async Task DeleteUserAsync_ShouldReturnTrue_WhenUserExists()
    {
        // Arrange
        var userId = 11223UL;
        var user = new UserModel
        {
            Id = userId,
            Timestamp = DateTime.UtcNow,
            Profiles = new List<UserProfile>
            {
                new()
                {
                    ProfileId = 1,
                    LtUid = 33333,
                    LToken = "delete-token"
                }
            }
        };
        await m_UserRepository.CreateOrUpdateUserAsync(user);

        // Act
        var result = await m_UserRepository.DeleteUserAsync(userId);
        var deletedUser = await m_UserRepository.GetUserAsync(userId);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(deletedUser, Is.Null);
    }

    [Test]
    public async Task DeleteUserAsync_ShouldReturnFalse_WhenUserDoesNotExist()
    {
        // Arrange
        var nonExistentUserId = 99999UL;

        // Act
        var result = await m_UserRepository.DeleteUserAsync(nonExistentUserId);

        // Assert
        Assert.That(result, Is.False);
    }
}