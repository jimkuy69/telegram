using Microsoft.Data.Sqlite;
using TelegramControlStudio.Core.Domain;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class AppUserRepositoryTests
{
    [Fact]
    public async Task UpsertGetListAndBlock()
    {
        var path = NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new AppUserRepository(path);
            var now = DateTimeOffset.UtcNow;

            Assert.Equal(0, await repository.CountAsync());
            Assert.Null(await repository.GetAsync(111));

            await repository.UpsertAsync(new AppUser(111, "Owner", "One", "owner1", AppUserRole.Owner, false, now, now));
            await repository.UpsertAsync(new AppUser(222, "Friend", "Two", null, AppUserRole.Friend, false, now, null));
            Assert.Equal(2, await repository.CountAsync());

            var owner = await repository.GetAsync(111);
            Assert.NotNull(owner);
            Assert.Equal(AppUserRole.Owner, owner!.Role);
            Assert.Equal("Owner One", owner.DisplayName);

            Assert.Equal(2, (await repository.ListAsync()).Count);

            await repository.UpsertAsync(owner with { FirstName = "Renamed" });
            Assert.Equal("Renamed", (await repository.GetAsync(111))!.FirstName);

            Assert.True(await repository.SetBlockedAsync(222, true));
            Assert.True((await repository.GetAsync(222))!.IsBlocked);
            Assert.False(await repository.SetBlockedAsync(999, true));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task TouchLoginUpdatesProfile()
    {
        var path = NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new AppUserRepository(path);
            await repository.UpsertAsync(new AppUser(333, "Old", "Name", null, AppUserRole.Friend, false, DateTimeOffset.UtcNow, null));

            var now = DateTimeOffset.UtcNow;
            await repository.TouchLoginAsync(333, "New", "Name", "newname", now);

            var user = await repository.GetAsync(333);
            Assert.Equal("New", user!.FirstName);
            Assert.Equal("newname", user.Username);
            Assert.NotNull(user.LastLoginAt);
        }
        finally
        {
            Cleanup(path);
        }
    }

    internal static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"telegram-appuser-{Guid.NewGuid():N}.db");

    internal static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class InviteRepositoryTests
{
    [Fact]
    public async Task CreatesAndReadsInvite()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new InviteRepository(path);
            var expires = DateTimeOffset.UtcNow.AddDays(7);

            var invite = await repository.CreateAsync(AppUserRole.Friend, "friend note", expires, 111);

            Assert.Equal(8, invite.Code.Length);
            Assert.Equal(invite.Code, invite.Code.ToUpperInvariant());
            var loaded = await repository.GetAsync(invite.Code.ToLowerInvariant());
            Assert.NotNull(loaded);
            Assert.Equal(AppUserRole.Friend, loaded!.Role);
            Assert.Equal("friend note", loaded.Note);
            Assert.True(loaded.IsRedeemable(DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task RedeemSucceedsOnceOnly()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new InviteRepository(path);
            var invite = await repository.CreateAsync(AppUserRole.Friend, null, null, null);

            Assert.True(await repository.RedeemAsync(invite.Code, 222, DateTimeOffset.UtcNow));
            Assert.False(await repository.RedeemAsync(invite.Code, 333, DateTimeOffset.UtcNow));

            var loaded = await repository.GetAsync(invite.Code);
            Assert.Equal(222, loaded!.UsedBy);
            Assert.False(loaded.IsRedeemable(DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task RedeemRejectsExpiredAndRevoked()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var repository = new InviteRepository(path);

            var expired = await repository.CreateAsync(AppUserRole.Friend, null, DateTimeOffset.UtcNow.AddMinutes(-1), null);
            Assert.False(await repository.RedeemAsync(expired.Code, 222, DateTimeOffset.UtcNow));

            var revokable = await repository.CreateAsync(AppUserRole.Friend, null, null, null);
            Assert.True(await repository.RevokeAsync(revokable.Code));
            Assert.False(await repository.RedeemAsync(revokable.Code, 222, DateTimeOffset.UtcNow));

            Assert.False(await repository.RevokeAsync(revokable.Code));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task RedeemRejectsUnknownCode()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            Assert.False(await new InviteRepository(path).RedeemAsync("ZZZZZZZZ", 222, DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }
}

public sealed class BotCommandRouterTests
{
    private const long OwnerId = 900001;
    private const long FriendId = 900002;

    [Fact]
    public async Task UnknownSenderGetsTheirTelegramId()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            var reply = await Route(path, 424242, "/status");
            Assert.Contains("424242", reply);
            Assert.Contains("ยังไม่ได้รับเชิญ", reply);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task BlockedUserIsDenied()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(FriendId, "Blocked", "User", null, AppUserRole.Friend, true, DateTimeOffset.UtcNow, null));

            Assert.Equal("บัญชีนี้ถูกระงับการใช้งาน", await Route(path, FriendId, "/me"));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task FriendCannotUseOwnerCommands()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(FriendId, "Friend", "User", null, AppUserRole.Friend, false, DateTimeOffset.UtcNow, null));

            var reply = await Route(path, FriendId, "/approve abc12345");
            Assert.Contains("เจ้าของระบบ", reply);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task FriendOptOutMutatesOwnContact()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await SeedAccountAsync(path);
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(FriendId, "Friend", "User", null, AppUserRole.Friend, false, DateTimeOffset.UtcNow, null));
            await new ContactRepository(path).UpsertAsync(
                new Contact(1, FriendId, "Friend", "User", null, null, true, false, DateTimeOffset.UtcNow));

            Assert.Contains("ไม่ได้รับข้อความ", await Route(path, FriendId, "/optout"));
            var contact = (await new ContactRepository(path).ListAsync(1)).Single(item => item.TelegramUserId == FriendId);
            Assert.True(contact.IsOptedOut);
            Assert.False(contact.HasConsent);

            Assert.Contains("ให้ความยินยอม", await Route(path, FriendId, "/optin"));
            contact = (await new ContactRepository(path).ListAsync(1)).Single(item => item.TelegramUserId == FriendId);
            Assert.False(contact.IsOptedOut);
            Assert.True(contact.HasConsent);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task OwnerApprovesWaitingJobByPrefix()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await SeedAccountAsync(path);
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            var jobId = await new JobRepository(path).CreateAsync(1, "ConsentMessaging", 2, Guid.NewGuid().ToString("N"));
            await new JobItemRepository(path).CreateAsync(new[]
            {
                Item(jobId, 101), Item(jobId, 102)
            });

            var reply = await Route(path, OwnerId, $"/approve {jobId.ToString()[..8]}");

            Assert.Contains("อนุมัติแล้ว", reply);
            Assert.Equal(JobStatus.Queued, (await new JobRepository(path).GetAsync(jobId))!.Status);
            Assert.All(await new JobItemRepository(path).ListAsync(jobId), item => Assert.Equal(JobStatus.Queued.ToString(), item.Status));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task OwnerCannotApproveJobThatIsNotWaiting()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await SeedAccountAsync(path);
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            var jobId = await new JobRepository(path).CreateAsync(1, "ConsentMessaging", 1, Guid.NewGuid().ToString("N"));
            await new JobRepository(path).UpdateStatusAsync(jobId, JobStatus.Completed);

            var reply = await Route(path, OwnerId, $"/approve {jobId.ToString()[..8]}");
            Assert.Contains("อนุมัติได้เฉพาะงานที่รออนุมัติ", reply);
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task OwnerCancelsPendingJob()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await SeedAccountAsync(path);
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            var jobId = await new JobRepository(path).CreateAsync(1, "ConsentMessaging", 2, Guid.NewGuid().ToString("N"));
            await new JobItemRepository(path).CreateAsync(new[] { Item(jobId, 101), Item(jobId, 102) });

            var reply = await Route(path, OwnerId, $"/cancel {jobId.ToString()[..8]}");

            Assert.Contains("ยกเลิกแล้ว", reply);
            Assert.Equal(JobStatus.Cancelled, (await new JobRepository(path).GetAsync(jobId))!.Status);
            Assert.All(await new JobItemRepository(path).ListAsync(jobId), item => Assert.Equal(JobStatus.Cancelled.ToString(), item.Status));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task OwnerInviteCreatesRedeemableFriendCode()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));
            await new AppSettingsRepository(path).SetAsync("public_base_url", "https://example.test");

            var reply = await Route(path, OwnerId, "/invite");

            Assert.Contains("https://example.test/invite/", reply);
            var invites = await new InviteRepository(path).ListAsync();
            var invite = Assert.Single(invites);
            Assert.Equal(AppUserRole.Friend, invite.Role);
            Assert.Equal(OwnerId, invite.CreatedBy);
            Assert.Contains(invite.Code, reply);
            Assert.True(invite.IsRedeemable(DateTimeOffset.UtcNow));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task OwnerCommandsWithoutArgumentExplainUsage()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            Assert.Equal("ใช้: /approve <id>", await Route(path, OwnerId, "/approve"));
            Assert.Equal("ใช้: /cancel <id>", await Route(path, OwnerId, "/cancel"));
            Assert.Equal("ใช้: /job <id>", await Route(path, OwnerId, "/job"));
            Assert.Equal("ใช้: /testmode on|off", await Route(path, OwnerId, "/testmode maybe"));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    [Fact]
    public async Task CommandsIgnoreBotMentions()
    {
        var path = AppUserRepositoryTests.NewDatabasePath();
        try
        {
            await new SqliteDatabase(path).InitializeAsync();
            await new AppUserRepository(path).UpsertAsync(
                new AppUser(OwnerId, "Owner", "User", null, AppUserRole.Owner, false, DateTimeOffset.UtcNow, null));

            Assert.Contains("เจ้าของ", await Route(path, OwnerId, "/help@my_control_bot"));
        }
        finally
        {
            AppUserRepositoryTests.Cleanup(path);
        }
    }

    private static Task<string> Route(string path, long fromId, string text) =>
        new BotCommandRouter(path).HandleAsync(new BotMessage(fromId, fromId, "Tester", null, text));

    private static JobItem Item(Guid jobId, long targetId) =>
        new(jobId, 1, targetId, JobStatus.WaitingForConfirmation.ToString(), 0, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow);

    private static async Task SeedAccountAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO accounts(display_name,phone_number,status,created_at) VALUES('test','+66000000000','Ready',$created_at);";
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
