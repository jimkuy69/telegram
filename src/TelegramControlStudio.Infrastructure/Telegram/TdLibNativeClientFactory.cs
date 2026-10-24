using System.Text.Json;
using TelegramControlStudio.Core.Telegram;

namespace TelegramControlStudio.Infrastructure.Telegram;

public sealed class TdLibNativeClientFactory(TdLibNativeLoader loader)
{
    public (TdLibNativeClient Client, int ClientId) CreateConfigured(
        string nativeDirectory,
        string expectedSha256,
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var handle = loader.Load(nativeDirectory, expectedSha256);
        var client = new TdLibNativeClient(handle);
        var clientId = client.CreateClientId();
        var request = new Dictionary<string, object?>(parameters)
        {
            ["@type"] = "setTdlibParameters"
        };
        client.Send(clientId, JsonSerializer.Serialize(request));
        return (client, clientId);
    }
}

