using TelegramControlStudio.Core.Import;
using Xunit;

namespace TelegramControlStudio.Tests;

public sealed class ContactCsvImporterTests
{
    [Fact]
    public void PreviewsValidAndInvalidRows()
    {
        var csv = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out\n42,A,B,user,,true,false\nbad,C,D,,,true,false";
        var result = ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Single(result.Errors);
        Assert.Equal(42, result.Valid[0].TelegramUserId);
    }

    [Fact]
    public void RejectsDuplicateTelegramIds()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var csv = $"{header}\n42,A,B,,,true,false\n42,C,D,,,true,false";
        var result = ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Contains("แถว 3: telegram_user_id 42 ซ้ำกับแถวอื่นในไฟล์นี้", result.Errors);
    }

    [Fact]
    public void RejectsInvalidPhoneAndNormalizesUsername()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var csv = $"{header}\n42, A , B , @user ,0812345678,true,false\n43, C , D , @valid , +66812345678 ,true,false";
        var result = ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("แถว 2: phone_number ไม่ถูกต้อง"));
        Assert.Equal("valid", result.Valid[0].Username);
        Assert.Equal("+66812345678", result.Valid[0].PhoneNumber);
    }

    // Excel drops trailing blank cells from a row, which used to come back as the same ROW_n_INVALID as
    // a bad id; the operator has to see that the consent columns are what is missing.
    [Fact]
    public void NamesTheMissingColumnsWhenARowIsShort()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var result = ContactCsvImporter.Preview($"{header}\n42,A,B,+66900000000", 7);
        Assert.Empty(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("แถว 2: อ่านได้ 4 คอลัมน์ ต้องมี 7 ตามหัวตาราง"));
    }

    [Fact]
    public void QuotesTheOffendingValueForAnUnusableTelegramId()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var result = ContactCsvImporter.Preview($"{header}\n1.23457E+11,A,B,user,,true,false", 7);
        Assert.Empty(result.Valid);
        Assert.Contains("แถว 2: telegram_user_id ต้องเป็นเลขจำนวนเต็มบวก (อ่านได้ \"1.23457E+11\")", result.Errors);
    }

    [Fact]
    public void RejectsANonBooleanConsentColumn()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var result = ContactCsvImporter.Preview($"{header}\n42,A,B,user,,yes,no", 7);
        Assert.Empty(result.Valid);
        Assert.Contains(result.Errors, error => error.Contains("แถว 2: has_consent และ is_opted_out ต้องเป็น true/false หรือ 1/0"));
    }

    [Fact]
    public void ReportsTheExpectedHeaderOnAHeaderMismatch()
    {
        var result = ContactCsvImporter.Preview("name,phone\nA,+66900000000", 7);
        Assert.Empty(result.Valid);
        var error = Assert.Single(result.Errors);
        Assert.Contains("หัวตารางไม่ถูกต้อง", error);
        Assert.Contains("has_consent", error);
    }

    [Fact]
    public void SupportsQuotedFieldsContainingCommas()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var result = ContactCsvImporter.Preview($"{header}\n42,\"Smith, Jr.\",Family,user,,true,false", 7);
        Assert.Single(result.Valid);
        Assert.Equal("Smith, Jr.", result.Valid[0].FirstName);
    }
}

