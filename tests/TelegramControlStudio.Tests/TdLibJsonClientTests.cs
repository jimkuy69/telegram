using System.Text.Json;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.Tests;

public sealed class TdLibJsonClientTests
{
    [Fact]
    public async Task CorrelatesResponseUsingExtra()
    {
        using var writer = new StringWriter();
        await using var client = new TdLibJsonClient(writer);
        var pending = client.SendAsync("getOption", new Dictionary<string, object?> { ["name"] = "version" });
        await Task.Delay(10);
        using var request = JsonDocument.Parse(writer.ToString());
        var extra = request.RootElement.GetProperty("@extra").GetString();
        Assert.True(client.HandleResponse($$"""{"@type":"optionValueString","value":"test","@extra":"{{extra}}"}"""));
        Assert.Contains("optionValueString", await pending);
    }
}
