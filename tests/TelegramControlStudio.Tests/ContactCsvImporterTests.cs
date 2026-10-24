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
        Assert.Contains("ROW_3_DUPLICATE", result.Errors);
    }

    [Fact]
    public void RejectsInvalidPhoneAndNormalizesUsername()
    {
        var header = "telegram_user_id,first_name,last_name,username,phone_number,has_consent,is_opted_out";
        var csv = $"{header}\n42, A , B , @user ,0812345678,true,false\n43, C , D , @valid , +66812345678 ,true,false";
        var result = ContactCsvImporter.Preview(csv, 7);
        Assert.Single(result.Valid);
        Assert.Contains("ROW_2_PHONE_INVALID", result.Errors);
        Assert.Equal("valid", result.Valid[0].Username);
        Assert.Equal("+66812345678", result.Valid[0].PhoneNumber);
    }
}

