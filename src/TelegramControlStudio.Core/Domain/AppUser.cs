namespace TelegramControlStudio.Core.Domain;

public enum AppUserRole
{
    Owner,
    Friend
}

public sealed record AppUser(
    long TelegramUserId,
    string FirstName,
    string LastName,
    string? Username,
    AppUserRole Role,
    bool IsBlocked,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt)
{
    public string DisplayName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
