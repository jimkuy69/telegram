using TelegramControlStudio.Core.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibParametersBuilderTests
{
    [Fact]
    public void BuildsRequiredTdlibParameters()
    {
        var result = TdLibParametersBuilder.Build(123, "hash", "db", "files");
        Assert.Equal(123, result["api_id"]);
        Assert.Equal("hash", result["api_hash"]);
        Assert.Equal(true, result["use_file_database"]);
        Assert.Equal(false, result["use_secret_chats"]);
        Assert.Equal("Telegram Control Studio", result["device_model"]);
        Assert.Equal("Windows", result["system_version"]);
    }

    [Fact]
    public void RejectsInvalidApiId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TdLibParametersBuilder.Build(0, "hash", "db", "files"));
}
