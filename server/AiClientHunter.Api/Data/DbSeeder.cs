using AiClientHunter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUser
            {
                Email = "admin@clienthunter.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 11),
                Role = "admin",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

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
