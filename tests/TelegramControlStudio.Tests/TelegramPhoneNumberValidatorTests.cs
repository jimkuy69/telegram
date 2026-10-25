using TelegramControlStudio.Core.Security;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TelegramPhoneNumberValidatorTests
{
    [Theory]
    [InlineData("+66 81-234-5678", "+66812345678")]
    [InlineData("+12025550123", "+12025550123")]
    public void NormalizesInternationalNumber(string input, string expected) => Assert.Equal(expected, TelegramPhoneNumberValidator.Normalize(input));

    [Theory]
    [InlineData("0812345678")]
    [InlineData("+0123456789")]
    [InlineData("+123")]
    public void RejectsNonInternationalNumber(string input) => Assert.Throws<FormatException>(() => TelegramPhoneNumberValidator.Normalize(input));
}

