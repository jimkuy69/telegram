namespace TelegramControlStudio.Core.Domain;

public sealed record TelegramGroup(
    long AccountId,
    long ChatId,
    string Title,
    string Kind,
    int? MemberCount,
    bool CanViewMembers,
    bool CanSendMessages,
    bool CanInviteUsers,
    DateTimeOffset? LastSyncedAt,
    string ObservedNote);

