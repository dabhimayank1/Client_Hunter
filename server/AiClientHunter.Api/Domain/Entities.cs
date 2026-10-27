using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AiClientHunter.Api.Domain;

public class Lead
{
    public long Id { get; set; }
    [MaxLength(300)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(60)] public string Country { get; set; } = "India";
    [MaxLength(500)] public string? WebsiteUrl { get; set; }
    [MaxLength(20)] public string WebsiteStatus { get; set; } = "unknown"; // verified|not_found|unknown
    public DateTime? WebsiteCheckedAt { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; } // +91XXXXXXXXXX
    [MaxLength(20)] public string LeadType { get; set; } = "potential"; // hot|warm|potential
    public int Score { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "new";
    [MaxLength(300)] public string? DedupeKey { get; set; }
    public DateTime? FollowUpDueAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<LeadEvidence> Evidence { get; set; } = new();
}

public class SearchJob
{
    public long Id { get; set; }
    [MaxLength(100)] public string City { get; set; } = "";
    [MaxLength(100)] public string Category { get; set; } = "";
    [MaxLength(50)] public string Source { get; set; } = "google"; // google | osm
    [MaxLength(20)] public string LeadType { get; set; } = "potential";
    public int ResultLimit { get; set; } = 50;
    [MaxLength(20)] public string Status { get; set; } = "queued"; // queued|running|completed|failed
    public int RecordsFound { get; set; }
    public int NewLeads { get; set; }
    public int Duplicates { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class SourceRecord
{
    public long Id { get; set; }
    [MaxLength(50)] public string SourceName { get; set; } = "";
    [MaxLength(100)] public string SourceId { get; set; } = "";
    [MaxLength(500)] public string SourceUrl { get; set; } = "";
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
    public string RawJson { get; set; } = "";
}

public class LeadEvidence
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    [JsonIgnore] public Lead? Lead { get; set; }
    public long SourceRecordId { get; set; }
    public SourceRecord? SourceRecord { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LeadScore
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    [JsonIgnore] public Lead? Lead { get; set; }
    public int Points { get; set; }
    [MaxLength(20)] public string Priority { get; set; } = "potential";
    public string Reasons { get; set; } = "";
    [MaxLength(50)] public string RuleVersion { get; set; } = "v1";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MessageDraft
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    [JsonIgnore] public Lead? Lead { get; set; }
    public int Version { get; set; } = 1;
    [MaxLength(50)] public string Channel { get; set; } = "whatsapp";
    [MaxLength(300)] public string? Subject { get; set; }
    public string Body { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "draft"; // draft|approved|rejected|sent
    [MaxLength(100)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CrmActivity
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    [JsonIgnore] public Lead? Lead { get; set; }
    [MaxLength(50)] public string Type { get; set; } = "note"; // call|message|reply|note
    [MaxLength(50)] public string Channel { get; set; } = "whatsapp";
    public string Note { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? FollowUpDueAt { get; set; }
}

public class DoNotContact
{
    public long Id { get; set; }
    [MaxLength(200)] public string Value { get; set; } = ""; // phone, email or domain
    [MaxLength(50)] public string Kind { get; set; } = "phone";
    [MaxLength(300)] public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    [MaxLength(100)] public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    [MaxLength(100)] public string Actor { get; set; } = "system";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppUser
{
    public long Id { get; set; }
    [MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(300)] public string PasswordHash { get; set; } = "";
    [MaxLength(50)] public string Role { get; set; } = "admin"; // admin|viewer
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
