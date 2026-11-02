using TelegramControlStudio.Core.Import;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Persistence;
using TelegramControlStudio.Infrastructure.Telegram;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class TdLibContactPhoneImportServiceTests
{
    [Fact]
    public void ParserReadsClientIdsAndSurfacesTelegramErrors()
    {
        var parsed = TdLibImportedContactsParser.Parse(
            """{"@type":"importedContacts","imported":[{"@type":"importedContact","user_id":77,"client_id":1}],"unimported_ids":[2],"failed_ids":[]}""");
        Assert.Null(parsed.Error);
        Assert.Equal(77, parsed.UserIdsByClientId[1]);
        Assert.Equal([1, 2], parsed.ClientIds);

        Assert.Contains("CHAT_ADMIN_REQUIRED", TdLibImportedContactsParser.Parse(
            """{"@type":"error","code":400,"message":"CHAT_ADMIN_REQUIRED","@extra":7}""").Error);
        Assert.NotNull(TdLibImportedContactsParser.Parse("""{"@type":"ok"}""").Error);
        Assert.NotNull(TdLibImportedContactsParser.Parse("not json").Error);
    }

    [Fact]
    public async Task NeverTouchesTelegramBeforeTheOperatorConfirms()
    {
        var client = new ScriptedClient(0);
        var result = await new TdLibContactPhoneImportService(TempPath()).ImportAsync(
            client, 1, [Row("+66810000001")], confirmed: false);
        Assert.Empty(client.BatchSizes);
        Assert.Equal(["CONFIRMATION_REQUIRED"], result.Errors);
    }

    [Fact]
    public async Task MapsZeroBasedClientIdsToTheRightPhone()
    {
        var result = await Resolve(0, 5);

        Assert.Equal(0, result.ClientIdBase);
        AssertEveryIdBelongsToItsOwnPhone(result);
        Assert.Equal(5, result.Resolved.Count);
    }

    [Fact]
    public async Task MapsOneBasedClientIdsWithoutShiftingThePhones()
    {
        var result = await Resolve(1, 5);

        Assert.Equal(1, result.ClientIdBase);
        AssertEveryIdBelongsToItsOwnPhone(result);
    }

    [Fact]
    public async Task StoresTheResolvedContactsAgainstTheAccount()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        var rows = Phones(1, 3).Select(Row).ToArray();

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ScriptedClient(0), accountId, rows, confirmed: true);

        var stored = await new ContactRepository(path).ListAsync(accountId);
        Assert.Equal(result.Resolved.Select(contact => contact.TelegramUserId).OrderBy(id => id),
            stored.Select(contact => contact.TelegramUserId).OrderBy(id => id));
        Assert.All(stored, contact => Assert.Equal(Phones(1, 3).First(phone => ExpectedId(phone) == contact.TelegramUserId), contact.PhoneNumber!));
    }

    [Fact]
    public async Task StopsRatherThanGuessWhenNoProbeNumberReturnsAClientId()
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        var client = new SilentClient();

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(client, accountId, Phones(1, 9).Select(Row).ToArray(), confirmed: true);

        Assert.Equal(3, client.CallCount);
        Assert.Empty(result.Resolved);
        Assert.Contains(result.Errors, error => error.Contains("client_id"));
        Assert.Empty(await new ContactRepository(path).ListAsync(accountId));
    }

    [Fact]
    public async Task SendsNoMoreThanTwoHundredNumbersPerRequest()
    {
        var client = new ScriptedClient(0);
        var path = TempPath();
        var accountId = await NewAccount(path);

        var result = await new TdLibContactPhoneImportService(path)
            .ImportAsync(client, accountId, Phones(1, 450).Select(Row).ToArray(), confirmed: true);

        Assert.Equal([1, 200, 200, 49], client.BatchSizes);
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
        // The probe and one full batch were answered before the throttle; the batch that was refused is
        // reported as unresolved rather than silently dropped.
        Assert.Equal(201, result.Resolved.Count);
        AssertEveryIdBelongsToItsOwnPhone(result);
        Assert.Contains(result.Unresolved, line => line.Contains("ไม่ได้ส่ง"));
        Assert.Equal(202, result.Resolved.Count + result.Unresolved.Count);
    }

    private static async Task<ContactPhoneImportResult> Resolve(int firstClientId, int count)
    {
        var path = TempPath();
        var accountId = await NewAccount(path);
        return await new TdLibContactPhoneImportService(path)
            .ImportAsync(new ScriptedClient(firstClientId), accountId, Phones(1, count).Select(Row).ToArray(), confirmed: true);
    }

    /// <summary>
    /// The invariant that matters: a phone must never end up with another person's Telegram id, which
    /// is exactly what a wrong client_id base would do.
    /// </summary>
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

    /// <summary>Answers with one imported contact per number, numbered from <paramref name="firstClientId"/>.</summary>
    private sealed class ScriptedClient(int firstClientId) : ITdLibClient
    {
        public List<int> BatchSizes { get; } = [];

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
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
            return Task.FromResult(Reply(contacts.Select(contact => (string?)contact["phone_number"]!), firstClientId));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string Reply(IEnumerable<string> phones, int firstClientId)
    {
        var imported = string.Join(",", phones.Select((phone, index) =>
            $$"""{"@type":"importedContact","user_id":{{long.Parse(phone.TrimStart('+'))}},"client_id":{{index + firstClientId}}}"""));
        return $$"""{"@type":"importedContacts","imported":[{{imported}}],"unimported_ids":[],"failed_ids":[]}""";
    }

    private sealed class SilentClient : ITdLibClient
    {
        public int CallCount { get; private set; }

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult("""{"@type":"importedContacts","imported":[],"unimported_ids":[],"failed_ids":[]}""");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrottlingClient : ITdLibClient
    {
        private int _calls;

        public Task<string> SendAsync(string function, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == 3)
                throw new TdLibFloodWaitException(37);
            var contacts = (List<Dictionary<string, object?>>)parameters["contacts"]!;
            return Task.FromResult(Reply(contacts.Select(contact => ((string?)contact["phone_number"])!), 0));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
