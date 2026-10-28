using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AiClientHunter.Api.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace AiClientHunter.Api.Services;

public class AuthService
{
    public const string DevOnlySecretPrefix = "dev-only-generated-secret-auth-";

    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public AuthService(IConfiguration config) : this(config, null)
    {
    }

    public AuthService(IConfiguration config, IHostEnvironment? env)
    {
        _issuer = config["Jwt:Issuer"] ?? "AiClientHunter";
        _audience = config["Jwt:Audience"] ?? "AiClientHunter";
        _expiryMinutes = int.TryParse(config["Jwt:ExpiryMinutes"], out var m) ? m : 480;

        var secret = config["Jwt:Secret"];
        var isDev = env?.IsDevelopment() ?? string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            if (!isDev)
            {
                throw new InvalidOperationException("Jwt:Secret configuration is missing or shorter than 32 characters in Production.");
            }
            secret = $"{DevOnlySecretPrefix}{Guid.NewGuid():N}{Guid.NewGuid():N}";
        }

        _secret = secret;
    }

    public string Secret => _secret;

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 11);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    public string GenerateJwtToken(AppUser user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secret);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = DateTime.UtcNow.AddMinutes(_expiryMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
