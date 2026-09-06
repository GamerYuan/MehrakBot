﻿﻿using Mehrak.GameApi.Shared;

namespace Mehrak.GameApi.Tests.Shared;

/// <summary>
/// Malformed credential values must be rejected before they can reach Cookie-header construction, where HttpHeaders.Add
/// would throw a FormatException embedding the credential into retained logs. </summary>
[TestFixture]
public class LTokenValidatorTests
{
    [Test]
    [TestCase("v2_abcDEF123")]
    [TestCase("token-with_dash.dot~tilde=equals+plus/slash")]
    [TestCase("!#$%&'*0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz")]
    [TestCase("a")]
    public void IsValidLToken_WellFormedValues_ReturnsTrue(string ltoken)
    {
        Assert.That(LTokenValidator.IsValidLToken(ltoken), Is.True);
    }

    [Test]
    [TestCase("canary\x01token", Description = "Control character: the audit's confirmed leak trigger")]
    [TestCase("canary\ntoken")]
    [TestCase("canary\rtoken")]
    [TestCase("canary token")]
    [TestCase("canary;token", Description = "Cookie pair separator")]
    [TestCase("canary,token")]
    [TestCase("canary\"token")]
    [TestCase("canary\\token")]
    [TestCase("canártoken", Description = "Non-ASCII")]
    [TestCase("canary\x7Ftoken", Description = "DEL")]
    public void IsValidLToken_MalformedValues_ReturnsFalse(string ltoken)
    {
        Assert.That(LTokenValidator.IsValidLToken(ltoken), Is.False);
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    public void IsValidLToken_NullOrEmpty_ReturnsFalse(string? ltoken)
    {
        Assert.That(LTokenValidator.IsValidLToken(ltoken), Is.False);
    }

    [Test]
    public void IsValidLToken_OverlongValue_ReturnsFalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LTokenValidator.IsValidLToken(new string('a', LTokenValidator.MaxLTokenLength)), Is.True);
            Assert.That(LTokenValidator.IsValidLToken(new string('a', LTokenValidator.MaxLTokenLength + 1)), Is.False);
        });
    }
}


