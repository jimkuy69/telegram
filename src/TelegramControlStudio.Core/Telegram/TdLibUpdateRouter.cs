using System.Text.Json;

namespace TelegramControlStudio.Core.Telegram;

public sealed class TdLibUpdateRouter(AuthorizationStateMachine authorizationStateMachine, Action<AuthorizationState>? onAuthorizationStateChanged = null)
{
    public void Route(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RouteDocument(document.RootElement);
        }
        catch (JsonException)
        {
            // A malformed native update must not terminate the receive loop.
        }
    }

    private void RouteDocument(JsonElement root)
    {
        if (!root.TryGetProperty("@type", out var type))
            return;

        if (type.GetString() == "updateAuthorizationState" &&
            root.TryGetProperty("authorization_state", out var state) &&
            state.TryGetProperty("@type", out var stateType))
        {
            authorizationStateMachine.Apply(stateType.GetString() ?? string.Empty);
            onAuthorizationStateChanged?.Invoke(authorizationStateMachine.Current);
        }
    }
}

