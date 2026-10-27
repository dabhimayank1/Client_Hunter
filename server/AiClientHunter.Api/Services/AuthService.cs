using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AiClientHunter.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace AiClientHunter.Api.Services;

public class AuthService(IConfiguration config)
{
    private readonly string _secret = config["Jwt:Secret"] ?? "AiClientHunter_Super_Secret_Jwt_Signing_Key_2026_Secure_Key!";
    private readonly string _issuer = config["Jwt:Issuer"] ?? "AiClientHunter";
    private readonly string _audience = config["Jwt:Audience"] ?? "AiClientHunter";
    private readonly int _expiryMinutes = int.TryParse(config["Jwt:ExpiryMinutes"], out var m) ? m : 1440;

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
