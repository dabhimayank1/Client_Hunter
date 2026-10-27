using AiClientHunter.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<SearchJob> SearchJobs => Set<SearchJob>();
    public DbSet<SourceRecord> SourceRecords => Set<SourceRecord>();
    public DbSet<LeadEvidence> EvidenceLinks => Set<LeadEvidence>();
    public DbSet<LeadScore> LeadScores => Set<LeadScore>();
    public DbSet<MessageDraft> MessageDrafts => Set<MessageDraft>();
    public DbSet<CrmActivity> CrmActivities => Set<CrmActivity>();
    public DbSet<DoNotContact> DoNotContactList => Set<DoNotContact>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Lead>().HasIndex(l => l.DedupeKey).IsUnique();
        b.Entity<Lead>().HasIndex(l => new { l.LeadType, l.Status });
        b.Entity<SourceRecord>().HasIndex(s => new { s.SourceName, s.SourceId }).IsUnique();
        b.Entity<DoNotContact>().HasIndex(d => d.Value).IsUnique();
        b.Entity<AppUser>().HasIndex(u => u.Email).IsUnique();
    }
}
