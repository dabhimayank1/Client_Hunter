using AiClientHunter.Api.Services;
using Xunit;

namespace AiClientHunter.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+91-98765-43210", "+919876543210")]
    [InlineData("079-65237760", "+917965237760")] // 11 digits starting with 0
    [InlineData("079-65237", null)] // Invalid landline (too short)
    [InlineData("9876543210 / 9123456789", "+919876543210")] // Multiple numbers separated by slash
    [InlineData("9876543210, 9123456789", "+919876543210")]  // Multiple numbers separated by comma
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("12345", null)] // Too short
    public void Phone_ShouldNormalize_Correctly(string? input, string? expected)
    {
        var result = Normalizer.Phone(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.example.com/about", "example.com")]
    [InlineData("http://example.com/", "example.com")]
    [InlineData("www.sub.example.in/path?q=1", "sub.example.in")]
    [InlineData("clinic.ahmedabad.org", "clinic.ahmedabad.org")]
    [InlineData("HTTPS://WWW.HOSPITAL.COM/INDEX.HTML", "hospital.com")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Domain_ShouldExtractCanonicalDomain(string? input, string? expected)
    {
        var result = Normalizer.Domain(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NameKey_ShouldPreserveUnicodeCharacters()
    {
        // Gujarati business name
        var gujarati = Normalizer.NameKey("રિદ્ધિ મેડિકલ નર્સિંગ હોમ!!!");
        Assert.Equal("રિદ્ધિ મેડિકલ નર્સિંગ હોમ", gujarati);

        // Hindi business name
        var hindi = Normalizer.NameKey("अपोलो फार्मेसी & क्लिनिक");
        Assert.Equal("अपोलो फार्मेसी क्लिनिक", hindi);

        // English business name with special symbols and multiple spaces
        var english = Normalizer.NameKey("  Little   Italy -- (Restaurant & Cafe)  ");
        Assert.Equal("little italy restaurant cafe", english);
    }

    [Theory]
    [InlineData("info@littleitaly.in", "info@littleitaly.in")]
    [InlineData("mailto:Contact@Hospital.Org", "contact@hospital.org")]
    [InlineData("dr.sharma@clinic.gujarat.in", "dr.sharma@clinic.gujarat.in")]
    [InlineData("support@biz.com, sales@biz.com", "support@biz.com")] // Multiple emails
    [InlineData("admin@biz.com; office@biz.com", "admin@biz.com")]
    [InlineData("invalid-email", null)]
    [InlineData("user@nodot", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Email_ShouldNormalize_Correctly(string? input, string? expected)
    {
        var result = Normalizer.Email(input);
        Assert.Equal(expected, result);
    }
}
