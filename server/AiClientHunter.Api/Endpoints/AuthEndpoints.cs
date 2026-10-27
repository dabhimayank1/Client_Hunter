using System.Security.Claims;
using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/auth");

        // 1. POST /api/auth/login
        g.MapPost("/login", async (LoginRequest req, AppDbContext db, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Email and password are required" });

            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

            if (user is null || !auth.VerifyPassword(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            var token = auth.GenerateJwtToken(user);

            db.AuditLogs.Add(new AuditLog
            {
                Action = "user_login",
                Actor = user.Email,
                Detail = $"User {user.Email} logged in successfully"
            });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.Role
                }
            });
        });

        // 2. POST /api/auth/register
        g.MapPost("/register", async (RegisterRequest req, AppDbContext db, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "Email and password are required" });

            if (req.Password.Length < 6)
                return Results.BadRequest(new { error = "Password must be at least 6 characters long" });

            var email = req.Email.Trim().ToLowerInvariant();
            var exists = await db.Users.AnyAsync(u => u.Email.ToLower() == email);
            if (exists)
                return Results.Conflict(new { error = "User with this email already exists" });

            var role = string.IsNullOrWhiteSpace(req.Role) ? "admin" : req.Role.Trim().ToLowerInvariant();

            var user = new AppUser
            {
                Email = email,
                PasswordHash = auth.HashPassword(req.Password),
                Role = role,
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var token = auth.GenerateJwtToken(user);

            return Results.Created($"/api/auth/me", new
            {
                message = "User registered successfully",
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.Role
                }
            });
        });

        // 3. GET /api/auth/me (Current user info)
        g.MapGet("/me", [Authorize] async (ClaimsPrincipal claimsUser, AppDbContext db) =>
        {
            var userIdStr = claimsUser.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.FindAsync(userId);
            if (user is null) return Results.NotFound();

            return Results.Ok(new
            {
                user.Id,
                user.Email,
                user.Role,
                user.CreatedAt
            });
        });
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password, string? Role);
