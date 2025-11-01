using Mehrak.Application.Models.Context;
using Mehrak.Bot.Services;
using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Bot.Tests.Services;

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

    #region Constructor Tests

    [Test]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        // Arrange & Act
        var validator = new TestParamValidator("testParam", "Test error message");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.ParamName, Is.EqualTo("testParam"));
            Assert.That(validator.ErrorMessage, Is.EqualTo("Test error message"));
        });
    }

    [Test]
    public void Constructor_WithEmptyParamName_AllowsEmptyString()
    {
        // Arrange & Act
        var validator = new TestParamValidator("", "Error message");

        // Assert
        Assert.That(validator.ParamName, Is.EqualTo(""));
    }

    [Test]
    public void Constructor_WithEmptyErrorMessage_AllowsEmptyString()
    {
        // Arrange & Act
        var validator = new TestParamValidator("param", "");

        // Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo(""));
    }

    #endregion

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

    [Test]
    public void IsValid_WithInvalidIntParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<int>("age", x => x >= 18);
        var context = CreateContext(("age", 15));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithZeroIntParameter_ValidatesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<int>("count", x => x > 0);
        var context = CreateContext(("count", 0));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithNegativeIntParameter_ValidatesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<int>("balance", x => x >= 0);
        var context = CreateContext(("balance", -10));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region IsValid Tests - Boolean Parameters

    [Test]
    public void IsValid_WithTrueBooleanParameter_ValidatesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<bool>("isActive", x => x == true);
        var context = CreateContext(("isActive", true));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithFalseBooleanParameter_ValidatesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<bool>("isActive", x => x == true);
        var context = CreateContext(("isActive", false));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region IsValid Tests - Complex Types

    [Test]
    public void IsValid_WithEnumParameter_ValidatesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<TestEnum>("status", x => x != TestEnum.Invalid);
        var context = CreateContext(("status", TestEnum.Active));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithInvalidEnumParameter_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<TestEnum>("status", x => x != TestEnum.Invalid);
        var context = CreateContext(("status", TestEnum.Invalid));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithObjectParameter_ValidatesProperties()
    {
        // Arrange
        var validator = new ParamValidator<TestObject>("data", x => x.Value > 0);
        var context = CreateContext(("data", new TestObject { Value = 10 }));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

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

    #region IsValid Tests - Predicate Variations

    [Test]
    public void IsValid_WithComplexPredicate_ExecutesCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<string>("email",
          x => !string.IsNullOrEmpty(x) && x.Contains("@") && x.Length > 5);
        var context = CreateContext(("email", "test@example.com"));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithAlwaysTruePredicate_AlwaysReturnsTrue()
    {
        // Arrange
        var validator = new ParamValidator<string>("param", x => true);
        var context = CreateContext(("param", "any value"));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithAlwaysFalsePredicate_AlwaysReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("param", x => false);
        var context = CreateContext(("param", "any value"));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValid_WithRangePredicate_ValidatesRange()
    {
        // Arrange
        var validator = new ParamValidator<int>("score", x => x is >= 0 and <= 100);
        var validContext = CreateContext(("score", 50));
        var invalidContextLow = CreateContext(("score", -1));
        var invalidContextHigh = CreateContext(("score", 101));

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(validContext), Is.True);
            Assert.That(validator.IsValid(invalidContextLow), Is.False);
            Assert.That(validator.IsValid(invalidContextHigh), Is.False);
        });
    }

    #endregion

    #region IsValid Tests - Multiple Parameters

    [Test]
    public void IsValid_WithMultipleParameters_ValidatesOnlySpecifiedParameter()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(
   ("name", "John"),
 ("age", 25),
        ("email", "john@example.com"));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsValid_WithWrongParameterInMultipleParams_ReturnsFalse()
    {
        // Arrange
        var validator = new ParamValidator<string>("name", x => !string.IsNullOrEmpty(x));
        var context = CreateContext(
         ("name", ""),
            ("age", 25));

        // Act
        var result = validator.IsValid(context);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region Error Message Tests

    [Test]
    public void ErrorMessage_WithCustomMessage_ReturnsCustomMessage()
    {
        // Arrange
        var validator = new ParamValidator<string>("email", x => x.Contains("@"), "Invalid email format");

        // Act & Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("Invalid email format"));
    }

    [Test]
    public void ErrorMessage_WithoutCustomMessage_ReturnsDefaultMessage()
    {
        // Arrange
        var validator = new ParamValidator<string>("username", x => x.Length > 3);

        // Act & Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("username cannot be empty"));
    }

    [Test]
    public void ErrorMessage_WithSpecialCharactersInParamName_IncludesInDefault()
    {
        // Arrange
        var validator = new ParamValidator<string>("user_name", x => x.Length > 0);

        // Act & Assert
        Assert.That(validator.ErrorMessage, Is.EqualTo("user_name cannot be empty"));
    }

    #endregion

    #region ParamName Tests

    [Test]
    public void ParamName_MatchesConstructorParameter()
    {
        // Arrange
        var validator = new ParamValidator<string>("testParameter", x => true);

        // Act & Assert
        Assert.That(validator.ParamName, Is.EqualTo("testParameter"));
    }

    [Test]
    public void ParamName_CaseSensitive_PreservesCase()
    {
        // Arrange
        var validator = new ParamValidator<string>("CamelCaseParam", x => true);

        // Act & Assert
        Assert.That(validator.ParamName, Is.EqualTo("CamelCaseParam"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void IsValid_CalledMultipleTimes_ReturnsSameResult()
    {
        // Arrange
        var validator = new ParamValidator<int>("value", x => x > 10);
        var context = CreateContext(("value", 15));

        // Act
        var result1 = validator.IsValid(context);
        var result2 = validator.IsValid(context);
        var result3 = validator.IsValid(context);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(result3, Is.True);
        });
    }

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

    #region Real-World Scenarios

    [Test]
    public void Validator_EmailValidation_WorksCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<string>("email",
         x => !string.IsNullOrEmpty(x) && x.Contains("@") && x.Contains("."),
            "Please enter a valid email address");

        var validContext = CreateContext(("email", "user@example.com"));
        var invalidContext1 = CreateContext(("email", "invalid-email"));
        var invalidContext2 = CreateContext(("email", ""));

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(validContext), Is.True);
            Assert.That(validator.IsValid(invalidContext1), Is.False);
            Assert.That(validator.IsValid(invalidContext2), Is.False);
            Assert.That(validator.ErrorMessage, Is.EqualTo("Please enter a valid email address"));
        });
    }

    [Test]
    public void Validator_AgeValidation_WorksCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<int>("age",
              x => x is >= 18 and <= 120,
            "Age must be between 18 and 120");

        var validContext = CreateContext(("age", 25));
        var tooYoungContext = CreateContext(("age", 17));
        var tooOldContext = CreateContext(("age", 121));

        // Act & Assert
        Assert.Multiple(() =>
   {
       Assert.That(validator.IsValid(validContext), Is.True);
       Assert.That(validator.IsValid(tooYoungContext), Is.False);
       Assert.That(validator.IsValid(tooOldContext), Is.False);
   });
    }

    [Test]
    public void Validator_UsernameValidation_WorksCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<string>("username",
      x => !string.IsNullOrEmpty(x) && x.Length >= 3 && x.Length <= 20,
     "Username must be between 3 and 20 characters");

        var validContext = CreateContext(("username", "john_doe"));
        var tooShortContext = CreateContext(("username", "ab"));
        var tooLongContext = CreateContext(("username", "this_username_is_way_too_long"));

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(validContext), Is.True);
            Assert.That(validator.IsValid(tooShortContext), Is.False);
            Assert.That(validator.IsValid(tooLongContext), Is.False);
        });
    }

    [Test]
    public void Validator_PasswordStrength_WorksCorrectly()
    {
        // Arrange
        var validator = new ParamValidator<string>("password",
               x => !string.IsNullOrEmpty(x) && x.Length >= 8 && x.Any(char.IsDigit) && x.Any(char.IsUpper),
       "Password must be at least 8 characters with a number and uppercase letter");

        var validContext = CreateContext(("password", "SecurePass123"));
        var tooShortContext = CreateContext(("password", "Short1"));
        var noNumberContext = CreateContext(("password", "NoNumberHere"));
        var noUpperContext = CreateContext(("password", "nouppercase123"));

        // Act & Assert
        Assert.Multiple(() =>
{
    Assert.That(validator.IsValid(validContext), Is.True);
    Assert.That(validator.IsValid(tooShortContext), Is.False);
    Assert.That(validator.IsValid(noNumberContext), Is.False);
    Assert.That(validator.IsValid(noUpperContext), Is.False);
});
    }

    #endregion

    #region Helper Methods

    private static ApplicationContextBase CreateContext(params (string key, object value)[] parameters)
    {
        return new ApplicationContextBase(TestUserId, parameters);
    }

    #endregion

    #region Helper Classes

    private class TestParamValidator : ParamValidator
    {
        public TestParamValidator(string paramName, string errorMessage)
  : base(paramName, errorMessage)
        {
        }

        public override bool IsValid(IApplicationContext context)
        {
            return true; // Simple implementation for testing base class
        }
    }

    private enum TestEnum
    {
        Invalid = 0,
        Active = 1,
        Inactive = 2
    }

    private class TestObject
    {
        public int Value { get; set; }
    }

    #endregion
}
