using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Sources;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Services;

public class DedupeService
{
    // Checks multi-tier deduplication stopping at the first match:
    // 1. Same SourceName + SourceId
    // 2. Same normalized DedupeKey (Domain or Name|City)
    // 3. Same normalized domain
    public async Task<(Lead? Lead, bool IsDuplicate)> FindExistingAsync(
        AppDbContext db,
        RawRecord r,
        string? domain,
        string dedupeKey,
        CancellationToken ct)
    {
        // 1. Same SourceName + SourceId already linked to a lead
        var linkedLead = await db.EvidenceLinks
            .Where(e => e.SourceRecord != null && e.SourceRecord.SourceName == r.SourceName && e.SourceRecord.SourceId == r.SourceId)
            .Select(e => e.Lead)
            .FirstOrDefaultAsync(ct);

        if (linkedLead != null)
            return (linkedLead, true);

        // 2. Match by normalized DedupeKey
        var existingByKey = await db.Leads
            .FirstOrDefaultAsync(l => l.DedupeKey == dedupeKey, ct);

        if (existingByKey != null)
            return (existingByKey, true);

        // 3. Match by normalized domain if present
        if (!string.IsNullOrWhiteSpace(domain))
        {
            var existingByDomain = await db.Leads
                .FirstOrDefaultAsync(l => l.WebsiteUrl != null && l.WebsiteUrl.Contains(domain), ct);

            if (existingByDomain != null)
                return (existingByDomain, true);
        }

        return (null, false);
    }
}
