#region

using MehrakCore.Services.Common;
using Moq;
using NetCord.Services;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures | ParallelScope.Children)]
public class AuthenticationResultTests
{
    private const ulong TestUserId = 123456789UL;
    private const ulong TestLtUid = 987654321UL; private const string TestLToken = "test_ltoken_value";
    private const string TestErrorMessage = "Test error message";

    private Mock<IInteractionContext> m_ContextMock;

    [SetUp]
    public void Setup()
    {
        m_ContextMock = new Mock<IInteractionContext>();
    }

    [Test]
    public void Success_ShouldCreateSuccessfulResult()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.Context, Is.Not.Null);
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
            Assert.That(result.Context, Is.Null);
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
            Assert.That(result.Context, Is.Null);
        });
    }
    [Test]
    public void Success_WithNullLToken_ShouldCreateResultWithNullLToken()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, null!, m_ContextMock.Object);        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.Null);
            Assert.That(result.Context, Is.Not.Null);
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
            Assert.That(result.Context, Is.Null);
        });
    }

    [Test]
    public void Success_WithEmptyLToken_ShouldCreateResultWithEmptyLToken()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, string.Empty, m_ContextMock.Object);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True); Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(string.Empty));
            Assert.That(result.Context, Is.Not.Null);
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
            Assert.That(result.Context, Is.Null);
        });
    }

    [Test]
    public void Success_WithNullContext_ShouldCreateResultWithNullContext()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, null!);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.UserId, Is.EqualTo(TestUserId));
            Assert.That(result.LtUid, Is.EqualTo(TestLtUid));
            Assert.That(result.LToken, Is.EqualTo(TestLToken));
            Assert.That(result.Context, Is.Null);
            Assert.That(result.ErrorMessage, Is.Null);
        });
    }

    [Test]
    public void Success_WithDifferentContexts_ShouldPreserveContextReference()
    {
        // Arrange
        var context1 = new Mock<IInteractionContext>();
        var context2 = new Mock<IInteractionContext>();

        // Act
        var result1 = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, context1.Object);
        var result2 = AuthenticationResult.Success(TestUserId + 1, TestLtUid + 1, TestLToken + "_2", context2.Object);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1.Context, Is.EqualTo(context1.Object));
            Assert.That(result2.Context, Is.EqualTo(context2.Object));
            Assert.That(result1.Context, Is.Not.EqualTo(result2.Context));
        });
    }

    [Test]
    public void Success_ContextProperty_ShouldNotBeNullWhenSuccessful()
    {
        // Act
        var result = AuthenticationResult.Success(TestUserId, TestLtUid, TestLToken, m_ContextMock.Object);

        // Assert - Using MemberNotNullWhen attribute validation
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Context, Is.Not.Null);
            Assert.That(result.LToken, Is.Not.Null);
        });
    }

    [Test]
    public void Failure_ContextProperty_ShouldBeNullWhenFailed()
    {
        // Act
        var result = AuthenticationResult.Failure(TestUserId, TestErrorMessage);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Context, Is.Null);
            Assert.That(result.LToken, Is.Null);
        });
    }

    [Test]
    public void Timeout_ContextProperty_ShouldBeNullWhenTimedOut()
    {
        // Act
        var result = AuthenticationResult.Timeout(TestUserId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Context, Is.Null);
            Assert.That(result.LToken, Is.Null);
        });
    }
}
