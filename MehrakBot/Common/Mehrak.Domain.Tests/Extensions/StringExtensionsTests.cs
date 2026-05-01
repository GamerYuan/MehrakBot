using Mehrak.Domain.Extensions;

namespace Mehrak.Domain.Tests.Extensions;

[TestFixture]
public class StringExtensionsTests
{
    [TestCase(null, ExpectedResult = null)]
    [TestCase("", ExpectedResult = "")]
    public string? ToTitleCase_NullOrEmpty_ReturnsAsIs(string? input)
    {
        return input?.ToTitleCase();
    }

    [TestCase("hello world", ExpectedResult = "Hello World")]
    [TestCase("HELLO WORLD", ExpectedResult = "Hello World")]
    [TestCase("hElLo wOrLd", ExpectedResult = "Hello World")]
    public string ToTitleCase_TwoWords_ReturnsTitleCased(string input)
    {
        return input.ToTitleCase();
    }

    [TestCase("hello_world", ExpectedResult = "Hello World")]
    [TestCase("HELLO_WORLD", ExpectedResult = "Hello World")]
    [TestCase("hello__world", ExpectedResult = "Hello World")]
    public string ToTitleCase_UnderscoreSeparated_ReturnsSpaceSeparated(string input)
    {
        return input.ToTitleCase();
    }

    [TestCase("a", ExpectedResult = "A")]
    [TestCase("abc", ExpectedResult = "Abc")]
    public string ToTitleCase_SingleWord_ReturnsTitleCased(string input)
    {
        return input.ToTitleCase();
    }

    [TestCase("   ", ExpectedResult = "")]
    [TestCase("___", ExpectedResult = "")]
    [TestCase(" _  _ ", ExpectedResult = "")]
    public string ToTitleCase_OnlySeparators_ReturnsEmpty(string input)
    {
        return input.ToTitleCase();
    }

    [TestCase(" hello ", ExpectedResult = "Hello")]
    [TestCase("_hello_", ExpectedResult = "Hello")]
    [TestCase("  hello  world  ", ExpectedResult = "Hello World")]
    public string ToTitleCase_LeadingTrailingSeparators_Trims(string input)
    {
        return input.ToTitleCase();
    }

    [TestCase("hello world foo", ExpectedResult = "Hello World Foo")]
    [TestCase("a_b_c_d", ExpectedResult = "A B C D")]
    public string ToTitleCase_ManyWords_ReturnsTitleCased(string input)
    {
        return input.ToTitleCase();
    }
}
