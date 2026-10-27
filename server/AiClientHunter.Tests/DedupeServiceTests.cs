using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using AiClientHunter.Api.Sources;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AiClientHunter.Tests;

public class DedupeServiceTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task FindExistingAsync_MatchesBySourceId()
    {
        using var db = CreateInMemoryDbContext();
        var lead = new Lead { Name = "Clinic A", City = "Ahmedabad", DedupeKey = "clinic a|ahmedabad" };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var source = new SourceRecord { SourceName = "osm", SourceId = "node/12345", RawJson = "{}" };
        db.SourceRecords.Add(source);
        await db.SaveChangesAsync();

        db.EvidenceLinks.Add(new LeadEvidence { LeadId = lead.Id, SourceRecordId = source.Id });
        await db.SaveChangesAsync();

        var dedupe = new DedupeService();
        var raw = new RawRecord("osm", "node/12345", "https://example.com/12345", "Clinic A", null, null, null, null, "{}", DateTime.UtcNow);

        var (matchedLead, isDuplicate) = await dedupe.FindExistingAsync(db, raw, null, "different key", CancellationToken.None);

        Assert.True(isDuplicate);
        Assert.NotNull(matchedLead);
        Assert.Equal(lead.Id, matchedLead.Id);
    }

    [Fact]
    public async Task FindExistingAsync_MatchesByDedupeKey()
    {
        using var db = CreateInMemoryDbContext();
        var lead = new Lead { Name = "Apollo Pharmacy", City = "Ahmedabad", DedupeKey = "apollo pharmacy|ahmedabad" };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var dedupe = new DedupeService();
        var raw = new RawRecord("osm", "node/99999", "https://example.com/99999", "Apollo Pharmacy", null, null, null, null, "{}", DateTime.UtcNow);

        var (matchedLead, isDuplicate) = await dedupe.FindExistingAsync(db, raw, null, "apollo pharmacy|ahmedabad", CancellationToken.None);

        Assert.True(isDuplicate);
        Assert.NotNull(matchedLead);
        Assert.Equal("Apollo Pharmacy", matchedLead.Name);
    }

    [Fact]
    public async Task FindExistingAsync_ReturnsFalse_ForNewUniqueLead()
    {
        using var db = CreateInMemoryDbContext();
        var dedupe = new DedupeService();
        var raw = new RawRecord("osm", "node/77777", "https://example.com/77777", "Brand New Cafe", null, null, null, null, "{}", DateTime.UtcNow);

        var (matchedLead, isDuplicate) = await dedupe.FindExistingAsync(db, raw, "newcafecity.com", "brand new cafe|surat", CancellationToken.None);

        Assert.False(isDuplicate);
        Assert.Null(matchedLead);
    }
}
