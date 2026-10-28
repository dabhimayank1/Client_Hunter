using AiClientHunter.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiClientHunter.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool isDevelopment = false, ILogger? logger = null)
    {
        if (!await db.Users.AnyAsync())
        {
            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                if (adminPassword.Length >= 10)
                {
                    db.Users.Add(new AppUser
                    {
                        Email = adminEmail.Trim().ToLowerInvariant(),
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, workFactor: 11),
                        Role = "admin",
                        CreatedAt = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                    logger?.LogInformation("Initial admin user seeded successfully for {Email}", adminEmail);
                }
                else
                {
                    logger?.LogWarning("ADMIN_PASSWORD is shorter than 10 characters. No admin user seeded.");
                }
            }
            else
            {
                logger?.LogWarning("ADMIN_EMAIL or ADMIN_PASSWORD environment variable not set. No admin user seeded.");
            }
        }

        // Keep the 3 sample leads only in Development
        if (!isDevelopment) return;

        if (await db.Leads.AnyAsync()) return;

        db.Leads.AddRange(
            new Lead
            {
                Name = "Sample Clinic A",
                Category = "clinic",
                City = "Ahmedabad",
                State = "Gujarat",
                WebsiteStatus = "not_found",
                LeadType = "potential",
                Score = 20,
                DedupeKey = "sample a|ahmedabad"
            },
            new Lead
            {
                Name = "Sample Startup B",
                Category = "software",
                City = "Surat",
                State = "Gujarat",
                WebsiteStatus = "unknown",
                LeadType = "warm",
                Score = 40,
                DedupeKey = "sample b|surat"
            },
            new Lead
            {
                Name = "Need website for my shop",
                Category = "retail",
                City = "Rajkot",
                State = "Gujarat",
                WebsiteStatus = "unknown",
                LeadType = "hot",
                Score = 85,
                DedupeKey = "sample c|rajkot"
            }
        );

        await db.SaveChangesAsync();
    }
}
