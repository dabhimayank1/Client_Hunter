using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AiClientHunter.Tests;

public class AuthServiceTests
{
    private class TestHostEnvironment(string envName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = envName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private AuthService CreateAuthService()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Jwt:Secret", "Test_Secret_Key_For_Unit_Tests_32_Bytes_Long_Key!"},
            {"Jwt:Issuer", "TestIssuer"},
            {"Jwt:Audience", "TestAudience"},
            {"Jwt:ExpiryMinutes", "60"}
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new AuthService(config);
    }

    [Fact]
    public void HashPassword_GeneratesValidBCryptHash()
    {
        var auth = CreateAuthService();
        var rawPassword = "SecurePassword@123";

        var hash = auth.HashPassword(rawPassword);

        Assert.NotNull(hash);
        Assert.StartsWith("$2", hash); // Standard BCrypt identifier
        Assert.True(auth.VerifyPassword(rawPassword, hash));
        Assert.False(auth.VerifyPassword("WrongPassword", hash));
    }

    [Fact]
    public void GenerateJwtToken_ContainsExpectedClaims()
    {
        var auth = CreateAuthService();
        var user = new AppUser
        {
            Id = 42,
            Email = "admin@example.com",
            Role = "admin"
        };

        var tokenString = auth.GenerateJwtToken(user);

        Assert.False(string.IsNullOrWhiteSpace(tokenString));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal("TestIssuer", jwt.Issuer);
        Assert.Contains("TestAudience", jwt.Audiences);

        var emailClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email");
        Assert.NotNull(emailClaim);
        Assert.Equal("admin@example.com", emailClaim.Value);

        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role");
        Assert.NotNull(roleClaim);
        Assert.Equal("admin", roleClaim.Value);
    }

    [Fact]
    public void Constructor_Throws_WhenSecretMissingInProduction()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var prodEnv = new TestHostEnvironment(Environments.Production);

        var ex = Assert.Throws<InvalidOperationException>(() => new AuthService(emptyConfig, prodEnv));
        Assert.Contains("shorter than 32 characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_Throws_WhenSecretTooShortInProduction()
    {
        var shortSecretConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Secret", "short-secret" }
            })
            .Build();
        var prodEnv = new TestHostEnvironment(Environments.Production);

        var ex = Assert.Throws<InvalidOperationException>(() => new AuthService(shortSecretConfig, prodEnv));
        Assert.Contains("shorter than 32 characters", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_GeneratesDevSecret_WhenSecretMissingInDevelopment()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var devEnv = new TestHostEnvironment(Environments.Development);

        var auth = new AuthService(emptyConfig, devEnv);

        Assert.NotNull(auth.Secret);
        Assert.True(auth.Secret.Length >= 32);
        Assert.StartsWith(AuthService.DevOnlySecretPrefix, auth.Secret);
    }
}
