using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

public sealed class TdLibUpdateRouter(AuthorizationStateMachine authorizationStateMachine)
{
    public void Route(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("@type", out var type))
            return;

        if (type.GetString() == "updateAuthorizationState" &&
            document.RootElement.TryGetProperty("authorization_state", out var state) &&
            state.TryGetProperty("@type", out var stateType))
        {
            authorizationStateMachine.Apply(stateType.GetString() ?? string.Empty);
        }
    }
}
