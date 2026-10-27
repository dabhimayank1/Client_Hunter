using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiClientHunter.Tests;

public class SuppressionTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task IsBlockedAsync_ReturnsTrue_WhenLeadStatusIsDoNotContact()
    {
        using var db = CreateInMemoryDbContext();
        var lead = new Lead { Name = "Blocked Clinic", Status = "do_not_contact" };

        var blocked = await Suppression.IsBlockedAsync(db, lead);

        Assert.True(blocked);
    }

    [Fact]
    public async Task IsBlockedAsync_ReturnsTrue_WhenPhoneIsInDoNotContactList()
    {
        using var db = CreateInMemoryDbContext();
        db.DoNotContactList.Add(new DoNotContact { Value = "+919876543210", Kind = "phone" });
        await db.SaveChangesAsync();

        var lead = new Lead { Name = "Customer A", Phone = "+919876543210", Status = "new" };

        var blocked = await Suppression.IsBlockedAsync(db, lead);

        Assert.True(blocked);
    }

    [Fact]
    public async Task IsBlockedAsync_ReturnsTrue_WhenDomainIsInDoNotContactList()
    {
        using var db = CreateInMemoryDbContext();
        db.DoNotContactList.Add(new DoNotContact { Value = "optout-firm.com", Kind = "domain" });
        await db.SaveChangesAsync();

        var lead = new Lead { Name = "Optout Firm", WebsiteUrl = "https://www.optout-firm.com/contact", Status = "new" };

        var blocked = await Suppression.IsBlockedAsync(db, lead);

        Assert.True(blocked);
    }

    [Fact]
    public async Task IsBlockedAsync_ReturnsFalse_ForCleanLead()
    {
        using var db = CreateInMemoryDbContext();
        var lead = new Lead { Name = "Legitimate Lead", Phone = "+919111122222", WebsiteUrl = "https://cleanbiz.in", Status = "new" };

        var blocked = await Suppression.IsBlockedAsync(db, lead);

        Assert.False(blocked);
    }
}
