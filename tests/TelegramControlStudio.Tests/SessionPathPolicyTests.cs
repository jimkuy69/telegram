using TelegramControlStudio.Core.Security;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class SessionPathPolicyTests
{
    [Fact]
    public void BuildsAccountScopedPathUnderLocalData()
    {
        var path = SessionPathPolicy.GetAccountSessionDirectory(42, @"C:\Users\Test\AppData\Local");
        Assert.True(SessionPathPolicy.IsWithinSessionRoot(path, @"C:\Users\Test\AppData\Local")); Assert.Contains(@"sessions\42", path);
    }

    [Fact]
    public void RejectsTraversalOutsideSessionRoot()
    {
        Assert.False(SessionPathPolicy.IsWithinSessionRoot(@"C:\Users\Test\AppData\Local\TelegramControlStudio\other", @"C:\Users\Test\AppData\Local"));
    }
}

