using TelegramControlStudio.Infrastructure.Security;
using System.Runtime.Versioning;
using Xunit;

namespace TelegramControlStudio.Tests;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStoreTests
{
    [Fact]
    public void RoundTripsSecretForCurrentUser()
    {
        var store = new DpapiSecretStore();
        var protectedValue = store.Protect("local-test-secret");

        Assert.NotEqual("local-test-secret", Convert.ToBase64String(protectedValue));
        Assert.Equal("local-test-secret", store.Unprotect(protectedValue));
    }
}
