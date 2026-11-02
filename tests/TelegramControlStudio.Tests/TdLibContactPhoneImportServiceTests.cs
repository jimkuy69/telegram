using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibContactPhoneImportServiceTests
{
    [Fact]
    public void ParserReadsPositionalUserIdsAndSurfacesTelegramErrors()
    {
        var parsed = TdLibImportedContactsParser.Parse(
            """{"@type":"importedContacts","user_ids":[77,0,88],"importer_count":[1,0,2]}""");
        Assert.Null(parsed.Error);
        Assert.Equal([77L, 0L, 88L], parsed.UserIds);

        Assert.Contains("CHAT_ADMIN_REQUIRED", TdLibImportedContactsParser.Parse(
            """{"@type":"error","code":400,"message":"CHAT_ADMIN_REQUIRED","@extra":7}""").Error);
        Assert.Contains("user_ids", TdLibImportedContactsParser.Parse(
            """{"@type":"importedContacts","imported":[]}""").Error);
        Assert.NotNull(TdLibImportedContactsParser.Parse("""{"@type":"ok"}""").Error);
        Assert.NotNull(TdLibImportedContactsParser.Parse("not json").Error);
    }

    [Fact]
    public async Task NeverTouchesTelegramBeforeTheOperatorConfirms()
    {
        var client = new ScriptedClient();
        var result = await new TdLibContactPhoneImportService(TempPath()).ImportAsync(
            client, 1, [Row("+66810000001")], confirmed: false);
        Assert.Empty(client.BatchSizes);
        Assert.Equal(["CONFIRMATION_REQUIRED"], result.Errors);
    }

    [Fact]
    public async Task MapsEachAnswerToItsOwnPhoneByPosition()
    {
        var result = await Resolve(5);

        AssertEveryIdBelongsToItsOwnPhone(result);
        Assert.Equal(5, result.Resolved.Count);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public async Task StoresTheResolvedContactsAgainstTheAccount()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        var rows = Phones(1, 3).Select(Row).ToArray();

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ScriptedClient(), accountId, rows, confirmed: true);

        var stored = await new ContactRepository(path).ListAsync(accountId);
        Assert.Equal(result.Resolved.Select(contact => contact.TelegramUserId).OrderBy(id => id),
            stored.Select(contact => contact.TelegramUserId).OrderBy(id => id));
        Assert.All(stored, contact => Assert.Equal(Phones(1, 3).First(phone => ExpectedId(phone) == contact.TelegramUserId), contact.PhoneNumber!));
    }

    [Fact]
    public async Task ReportsNumbersTelegramDoesNotRecogniseWithoutFailingTheRequest()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        var client = new SilentClient();

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(client, accountId, Phones(1, 9).Select(Row).ToArray(), confirmed: true);

        Assert.Equal(1, client.CallCount);
        Assert.Empty(result.Resolved);
        Assert.Empty(result.Errors);
        Assert.Equal(9, result.Unresolved.Count);
        Assert.Empty(await new ContactRepository(path).ListAsync(accountId));
    }

    [Fact]
    public async Task SendsNoMoreThanTwoHundredNumbersPerRequest()
    {
        var client = new ScriptedClient();
        var path = TempPath();
        var accountId = await NewAccount(path);

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(client, accountId, Phones(1, 450).Select(Row).ToArray(), confirmed: true);

        Assert.Equal([200, 200, 50], client.BatchSizes);
        Assert.Equal(450, result.Resolved.Count);
        AssertEveryIdBelongsToItsOwnPhone(result);
    }

    [Fact]
    public async Task ReportsTheRowsItNeverGotToSendWhenTelegramThrottles()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ThrottlingClient(), accountId, Phones(1, 202).Select(Row).ToArray(), confirmed: true);

        Assert.Contains(result.Errors, error => error.Contains("FLOOD_WAIT"));
        // One full batch was answered before the throttle; the batch that was refused is reported as
        // unresolved rather than silently dropped.
        Assert.Equal(200, result.Resolved.Count);
        AssertEveryIdBelongsToItsOwnPhone(result);
        Assert.Contains(result.Unresolved, line => line.Contains("ไม่ได้ส่ง"));
        Assert.Equal(202, result.Resolved.Count + result.Unresolved.Count);
    }

    [Fact]
    public async Task DropsTheBatchWhenItsFirstIdNamesAnotherPhone()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ScriptedClient(_ => "+66999999999"), accountId, Phones(1, 5).Select(Row).ToArray(), confirmed: true);

        Assert.Empty(result.Resolved);
        Assert.Empty(await new ContactRepository(path).ListAsync(accountId));
        Assert.Contains(result.Errors, error => error.Contains("ไม่ตรง"));
        Assert.Equal(5, result.Unresolved.Count(line => line.Contains("ชุดนี้")));
    }

    private static async Task<ContactPhoneImportResult> Resolve(int count)
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        return await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ScriptedClient(), accountId, Phones(1, count).Select(Row).ToArray(), confirmed: true);
    }

    private static void AssertEveryIdBelongsToItsOwnPhone(ContactPhoneImportResult result) =>
        Assert.All(result.Resolved, contact =>
            Assert.Equal(ExpectedId(contact.PhoneNumber!), contact.TelegramUserId));

    private static PhoneOnlyContact Row(string phone) => new(1, "สมชาย", "ใจดี", null, phone, false, false);

    private static IReadOnlyList<string> Phones(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(index => $"+668{index:D8}").ToArray();

    private static long ExpectedId(string phone) => long.Parse(phone.TrimStart('+'));

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"tg-phone-import-{Guid.NewGuid():N}.db");

    private static async Task<long> NewAccount(string path)
    {
        await new SqliteDatabase(path).InitializeAsync();
        return await new AccountRepository(path).CreateAsync("Import", "+66000000001");
    }

    /// <summary>Answers with the phone digits as the user id of each contact, in request order.</summary>
    private sealed class ScriptedClient(Func<long, string>? reportedPhone = null) : ITdLibClient
    {
        public List<int> BatchSizes { get; } = [];

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            if (function == "getUser")
                return Task.FromResult(User((long)parameters["user_id"]!,
                    reportedPhone?.Invoke((long)parameters["user_id"]!) ?? $"+{(long)parameters["user_id"]!}"));
            Assert.Equal("importContacts", function);
            var contacts = (List<Dictionary<string, object?>>)parameters["contacts"]!;
            Assert.True(contacts.Count <= 200);
            BatchSizes.Add(contacts.Count);
            foreach (var contact in contacts)
            {
                Assert.Equal("contact", contact["@type"]);
                Assert.Equal(0L, Assert.IsType<long>(contact["user_id"]));
                Assert.False(string.IsNullOrWhiteSpace((string?)contact["phone_number"]));
            }
            return Task.FromResult(Reply(contacts.Select(contact => (string?)contact["phone_number"]!)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string Reply(IEnumerable<string> phones) =>
        $$"""{"@type":"importedContacts","user_ids":[{{string.Join(",", phones.Select(phone => long.Parse(phone.TrimStart('+'))))}}],"importer_count":[]}""";

    private static string User(long userId, string phone) =>
        $$"""{"@type":"user","id":{{userId}},"first_name":"","last_name":"","phone_number":"{{phone}}"}""";

    /// <summary>Answers that none of the numbers belongs to a registered Telegram user.</summary>
    private sealed class SilentClient : ITdLibClient
    {
        public int CallCount { get; private set; }

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var contacts = (List<Dictionary<string, object?>>)parameters["contacts"]!;
            return Task.FromResult($$"""{"@type":"importedContacts","user_ids":[{{string.Join(",", Enumerable.Repeat(0, contacts.Count))}}],"importer_count":[]}""");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrottlingClient : ITdLibClient
    {
        private int _imports;

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            if (function == "getUser")
                return Task.FromResult(User((long)parameters["user_id"]!, $"+{(long)parameters["user_id"]!}"));
            _imports++;
            if (_imports == 2)
                throw new TdLibFloodWaitException(37);
            var contacts = (List<Dictionary<string, object?>>)parameters["contacts"]!;
            return Task.FromResult(Reply(contacts.Select(contact => ((string?)contact["phone_number"])!)));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
