using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class BotTokenSetupPolicyTests
{
    private const string Dead = "111:AAAAAAAAAAAAAAAAAAAA";
    private const string Fresh = "222:BBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void FirstSetupIsSaved()
    {
        Assert.Equal(BotTokenSetupDecision.Save,
            BotTokenSetupPolicy.Decide(null, Fresh, null, storedTokenAccepted: false, hasUsers: false));
    }

    [Fact]
    public void ResubmittingTheSameTokenRefreshesMetadata()
    {
        Assert.Equal(BotTokenSetupDecision.Save,
            BotTokenSetupPolicy.Decide(Dead, Dead, null, storedTokenAccepted: false, hasUsers: true));
    }

    [Fact]
    public void WorkingTokenIsNotReplaced()
    {
        Assert.Equal(BotTokenSetupDecision.AlreadyConfigured,
            BotTokenSetupPolicy.Decide(Dead, Fresh, Dead, storedTokenAccepted: true, hasUsers: true));
    }

    [Fact]
    public void DeadTokenIsReplacedWhileNoUsersExist()
    {
        Assert.Equal(BotTokenSetupDecision.Save,
            BotTokenSetupPolicy.Decide(Dead, Fresh, null, storedTokenAccepted: false, hasUsers: false));
    }

    [Fact]
    public void DeadTokenNeedsThePreviousTokenOnceUsersExist()
    {
        Assert.Equal(BotTokenSetupDecision.RequiresPreviousToken,
            BotTokenSetupPolicy.Decide(Dead, Fresh, null, storedTokenAccepted: false, hasUsers: true));
        Assert.Equal(BotTokenSetupDecision.RequiresPreviousToken,
            BotTokenSetupPolicy.Decide(Dead, Fresh, "999:wrong", storedTokenAccepted: false, hasUsers: true));
        Assert.Equal(BotTokenSetupDecision.Save,
            BotTokenSetupPolicy.Decide(Dead, Fresh, Dead, storedTokenAccepted: false, hasUsers: true));
    }
}
