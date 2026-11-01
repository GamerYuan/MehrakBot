#region

using Mehrak.Bot.Shared.Models;
using Mehrak.Bot.Shared.Services;
using Moq;

#endregion

namespace Mehrak.Bot.Tests.Shared.Services;

/// <summary>
/// Unit tests for ParamValidator and ParamValidator&lt;TParam&gt; validating parameter validation logic,
/// predicate execution, error messages, and type handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ParamValidatorTests
{
    private const ulong TestUserId = 123456789UL;

    #region Generic ParamValidator Constructor Tests

    [Test]
    public void GenericConstructor_WithValidParameters_SetsProperties()
    {
        // Arrange & Act
        var validator = new ParamValidator<string>("name", x => x.Length > 0, "Name is required");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.ParamName, Is.EqualTo("name"));
            Assert.That(validator.ErrorMessage, Is.EqualTo("Name is required"));
        });
    }

    [Test]
    public void GenericConstructor_WithoutErrorMessage_UsesDefaultMessage()
    {
        // Arrange & Act
        var validator = new ParamValidator<string>("testParam", x => x.Length > 0);

        // Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("testParam cannot be empty"));
    }

    [Test]
    public void GenericConstructor_WithNullPredicate_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ParamValidator<string>("param", null!));
    }

    [Test]
    public void GenericConstructor_WithNullErrorMessage_UsesDefaultMessage()
    {
        // Arrange & Act
        var validator = new ParamValidator<string>("param", x => true, null);

        // Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("param cannot be empty"));
    }

    #endregion

    #region IsValid Tests - String Parameters

    [Test]
    public void IsValid_WithValidStringParameter_ReturnsTrue()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(("name", "ValidName"));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithInvalidStringParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(("name", ""));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithMissingParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(); // No parameters

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithNullStringParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => x != null);
        var context = CreateContext(("name", null!));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region IsValid Tests - Numeric Parameters

    [Test]
    public void IsValid_WithValidIntParameter_ReturnsTrue()
    {
        // Arrange
        var validator = new ParamValidator<int>("age", x => x >= 18);
        var context = CreateContext(("age", 25));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    #endregion

    #region IsValid Tests - Complex Types

    [Test]
    public void IsValid_WithNullObjectParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<TestObject>("data", x => x != null && x.Value > 0);
        var context = CreateContext(("data", null!));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region IsValid Tests - Type Mismatch

    [Test]
    public void IsValid_WithWrongParameterType_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<int>("age", x => x >= 18);
        var context = CreateContext(("age", "25")); // String instead of int

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithIncompatibleType_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(("name", 123)); // int instead of string

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Error Message Tests

    [Test]
    public void ErrorMessage_WithSpecialCharactersInParamName_IncludesInDefault()
    {
        // Arrange
        var validator = new ParamValidator<string>("user_name", x => x.Length > 0);

        // Act & Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("user_name cannot be empty"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void IsValid_WithDifferentContexts_ValidatesIndependently()
    {
        // Arrange
        var validator = new ParamValidator<int>("value", x => x > 10);
        var validContext = CreateContext(("value", 15));
        var invalidContext = CreateContext(("value", 5));

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(validContext), Is.True);
            Assert.That(validator.IsValid(invalidContext), Is.False);
        });
    }

    [Test]
    public void IsValid_WithNullableType_HandlesNullCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<int?>("nullableValue", x => x.HasValue && x.Value > 0);
        var validContext = CreateContext(("nullableValue", (int?)10));
        var nullContext = CreateContext(("nullableValue", null!));

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(validContext), Is.True);
            Assert.That(validator.IsValid(nullContext), Is.False);
        });
    }

    #endregion

    #region Helper Methods

    private static ICommandExecutorService CreateContext(params (string key, object value)[] parameters)
    {
        var mock = new Mock<ICommandExecutorService>();
        var dict = parameters.ToDictionary(k => k.key, v => v.value);
        mock.Setup(x => x.Parameters).Returns(dict);
        return mock.Object;
    }

    #endregion

    #region Helper Classes

    private class TestObject
    {
        public int Value { get; set; }
    }

    #endregion
}
