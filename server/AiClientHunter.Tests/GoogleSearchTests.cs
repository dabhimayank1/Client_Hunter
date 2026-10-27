using AiClientHunter.Api.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiClientHunter.Tests;

public class GoogleSearchTests
{
    [Fact]
    public void GoogleWebSearchAdapter_HasCorrectName()
    {
        var adapter = new GoogleWebSearchAdapter(new HttpClient(), NullLogger<GoogleWebSearchAdapter>.Instance);
        Assert.Equal("google", adapter.Name);
    }

    [Theory]
    [InlineData("justdial.com", true)]
    [InlineData("www.justdial.com", true)]
    [InlineData("practo.com", true)]
    [InlineData("indiamart.com", true)]
    [InlineData("facebook.com", true)]
    [InlineData("instagram.com", true)]
    [InlineData("drshahdental.com", false)]
    [InlineData("apexhearthospital.in", false)]
    public void IsDirectoryDomain_CorrectlyFlagsAggregators(string domain, bool expectedIsDirectory)
    {
        // Directory domains mean the business does NOT have its own website
        var method = typeof(GoogleWebSearchAdapter).GetMethod("IsDirectoryDomain", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var result = (bool)method.Invoke(null, new object[] { domain })!;
        Assert.Equal(expectedIsDirectory, result);
    }

    [Theory]
    [InlineData("Call now at +91 98765 43210 for appointment", "+91 98765 43210")]
    [InlineData("Contact Dr. Patel at 079-26567890 Ahmedabad", "079-26567890")]
    [InlineData("No phone mentioned here", null)]
    public void ExtractPhone_FindsIndianPhones(string text, string? expected)
    {
        var method = typeof(GoogleWebSearchAdapter).GetMethod("ExtractPhone", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var result = (string?)method.Invoke(null, new object[] { text });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Write to us at info@careclinic.org for inquiries", "info@careclinic.org")]
    [InlineData("doctor.sharma99@gmail.com is official email", "doctor.sharma99@gmail.com")]
    [InlineData("No email provided", null)]
    public void ExtractEmail_FindsEmail(string text, string? expected)
    {
        var method = typeof(GoogleWebSearchAdapter).GetMethod("ExtractEmail", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var result = (string?)method.Invoke(null, new object[] { text });
        Assert.Equal(expected, result);
    }
}
