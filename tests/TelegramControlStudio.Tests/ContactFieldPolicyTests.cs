using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactFieldPolicyTests
{
    [Theory]
    [InlineData("+66812345678", true)]
    [InlineData("+12025550123", true)]
    [InlineData("0812345678", false)]
    [InlineData("+123", false)]
    public void ValidatesE164Phone(string phone, bool expected) =>
        Assert.Equal(expected, ContactFieldPolicy.IsValidPhone(phone));
}

