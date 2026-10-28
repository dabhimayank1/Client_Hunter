using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiClientHunter.Api.Endpoints;

public static class DraftEndpoints
{
    public static void MapDraftEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/drafts").RequireAuthorization();

        // 1. GET /api/drafts (Approval Queue list, skips suppressed leads)
        g.MapGet("", async (AppDbContext db, string? status) =>
        {
            var query = db.MessageDrafts
                .Include(d => d.Lead)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(d => d.Status == status.Trim().ToLowerInvariant());

            var drafts = await query
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            // Filter out leads that have been suppressed in DoNotContact list
            var allowedDrafts = new List<object>();
            foreach (var d in drafts)
            {
                if (d.Lead != null && !await Suppression.IsBlockedAsync(db, d.Lead))
                {
                    allowedDrafts.Add(new
                    {
                        d.Id,
                        d.LeadId,
                        d.Version,
                        d.Channel,
                        d.Subject,
                        d.Body,
                        d.Status,
                        d.ApprovedBy,
                        d.ApprovedAt,
                        d.CreatedAt,
                        Lead = new
                        {
                            d.Lead.Id,
                            d.Lead.Name,
                            d.Lead.City,
                            d.Lead.State,
                            d.Lead.Category,
                            d.Lead.Phone,
                            d.Lead.Email,
                            d.Lead.WebsiteUrl,
                            d.Lead.Score,
                            d.Lead.Status
                        }
                    });
                }
            }

            return Results.Ok(allowedDrafts);
        });

        // 2. GET /api/drafts/stats (Daily soft counter: tracks sent today vs limit 20)
        g.MapGet("stats", async (AppDbContext db) =>
        {
            var todayUtc = DateTime.UtcNow.Date;
            var sentToday = await db.CrmActivities
                .Where(a => a.Type == "message" && a.OccurredAt >= todayUtc)
                .CountAsync();

            var pendingApproval = await db.MessageDrafts
                .Where(d => d.Status == "draft")
                .CountAsync();

            var approvedReady = await db.MessageDrafts
                .Where(d => d.Status == "approved")
                .CountAsync();

            return Results.Ok(new
            {
                sentToday,
                dailyLimit = 20,
                isLimitExceeded = sentToday >= 20,
                pendingApproval,
                approvedReady
            });
        });

        // 3. PUT /api/drafts/{id:long} (Edit draft body: resets approved draft back to 'draft')
        g.MapPut("{id:long}", async (long id, UpdateDraftRequest req, AppDbContext db) =>
        {
            var d = await db.MessageDrafts.Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();

            d.Body = req.Body.Trim();
            if (!string.IsNullOrWhiteSpace(req.Subject)) d.Subject = req.Subject.Trim();
            if (!string.IsNullOrWhiteSpace(req.Channel)) d.Channel = req.Channel.Trim();

            // Guardrail from Guide Page 32: An edit to an approved draft returns it to "draft" so it must be approved again
            if (d.Status == "approved")
            {
                d.Status = "draft";
                d.ApprovedBy = null;
                d.ApprovedAt = null;
                if (d.Lead != null) d.Lead.Status = "draft_ready";
            }

            await db.SaveChangesAsync();
            return Results.Ok(d);
        });

        // 4. POST /api/drafts/{id:long}/approve (Human Approval Gate)
        g.MapPost("{id:long}/approve", async (long id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var d = await db.MessageDrafts.Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();

            if (d.Status != "draft")
                return Results.BadRequest(new { error = "Only drafts can be approved" });

            if (d.Lead != null && await Suppression.IsBlockedAsync(db, d.Lead))
                return Results.Conflict(new { error = "Lead is on the do-not-contact suppression list" });

            var approver = user.Identity?.Name ?? "local-user";

            d.Status = "approved";
            d.ApprovedBy = approver;
            d.ApprovedAt = DateTime.UtcNow;

            if (d.Lead != null)
            {
                d.Lead.Status = "approved";
                d.Lead.UpdatedAt = DateTime.UtcNow;
            }

            db.AuditLogs.Add(new AuditLog
            {
                Action = "draft_approved",
                Actor = approver,
                Detail = $"Draft #{id} approved for lead #{d.LeadId} ({d.Lead?.Name})"
            });

            await db.SaveChangesAsync();
            return Results.Ok(d);
        });

        // 5. POST /api/drafts/{id:long}/mark-sent (Manual Outreach Confirmation)
        g.MapPost("{id:long}/mark-sent", async (long id, AppDbContext db, ClaimsPrincipal claimsUser) =>
        {
            var d = await db.MessageDrafts.Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();

            // Strict rule: No message can be marked sent unless the draft was explicitly approved!
            if (d.Status != "approved")
                return Results.BadRequest(new { error = "Draft must be approved before it can be marked as sent" });

            d.Status = "sent";

            if (d.Lead != null)
            {
                d.Lead.Status = "contacted";
                d.Lead.FollowUpDueAt = DateTime.UtcNow.AddDays(3);
                d.Lead.UpdatedAt = DateTime.UtcNow;

                db.CrmActivities.Add(new CrmActivity
                {
                    LeadId = d.Lead.Id,
                    Type = "message",
                    Channel = d.Channel,
                    Note = $"Manual outreach message sent via {d.Channel}: {d.Subject}",
                    OccurredAt = DateTime.UtcNow,
                    FollowUpDueAt = d.Lead.FollowUpDueAt
                });
            }

            var actor = claimsUser.Identity?.Name
                        ?? claimsUser.FindFirstValue(ClaimTypes.Name)
                        ?? claimsUser.FindFirstValue(ClaimTypes.Email)
                        ?? "user";

            db.AuditLogs.Add(new AuditLog
            {
                Action = "draft_marked_sent",
                Actor = actor,
                Detail = $"Outreach confirmed sent for lead #{d.LeadId}"
            });

            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Draft marked as sent. CRM activity recorded and follow-up scheduled for 3 days.", draft = d });
        });

        // 6. POST /api/drafts/{id:long}/reject (Discard Draft)
        g.MapPost("{id:long}/reject", async (long id, AppDbContext db) =>
        {
            var d = await db.MessageDrafts.Include(x => x.Lead).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();

            d.Status = "rejected";
            if (d.Lead != null)
            {
                d.Lead.Status = "needs_review";
                d.Lead.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            return Results.Ok(d);
        });
    }
}

public record UpdateDraftRequest(string Body, string? Subject, string? Channel);
