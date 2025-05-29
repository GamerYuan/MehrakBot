#region

using MehrakCore.Services.Common;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures | ParallelScope.Children)]
public class AuthenticationResultTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL;
    private const string TestLToken = "test_ltoken_value";
    private const string TestErrorMessage = "Test error message";

    [Test]
    public void Success_ShouldCreateSuccessfulResult()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void Failure_ShouldCreateFailureResult()
    {
        // Act
        var result = AuthenticationResult.Failure(TestUserId, TestErrorMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.ErrorMessage, Is.EqualTo(TestErrorMessage));
            Assert.That(result.LtUid, Is.EqualTo(0UL)); // Default value
            Assert.That(result.LToken, Is.Null);
        });
    }

    [Test]
    public void Timeout_ShouldCreateTimeoutResult()
    {
        // Act
        var result = AuthenticationResult.Timeout(TestUserId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.ErrorMessage, Is.EqualTo("Authentication timed out"));
            Assert.That(result.LtUid, Is.EqualTo(0UL)); // Default value
            Assert.That(result.LToken, Is.Null);
        });
    }

    [Test]
    public void Success_WithNullLToken_ShouldCreateResultWithNullLToken()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, null!);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.Null);
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void Failure_WithNullErrorMessage_ShouldCreateResultWithNullError()
    {
        // Act
        var result = AuthenticationResult.Failure(TestUserId, null!);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.LtUid, Is.EqualTo(0UL));
            Assert.That(result.LToken, Is.Null);
        });
    }

    [Test]
    public void Success_WithEmptyLToken_ShouldCreateResultWithEmptyLToken()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, string.Empty);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(string.Empty));
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void Failure_WithEmptyErrorMessage_ShouldCreateResultWithEmptyError()
    {
        // Act
        var result = AuthenticationResult.Failure(TestUserId, string.Empty);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(result.LtUid, Is.EqualTo(0UL));
            Assert.That(result.LToken, Is.Null);
        });
    }
}
