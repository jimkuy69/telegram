namespace TelegramControlStudio.Core.Domain;

public sealed record AccountSecret(
    long AccountId,
    string SecretName,
    byte[] ProtectedValue,
    DateTimeOffset UpdatedAt);
