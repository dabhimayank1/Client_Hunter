using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Jobs;
using Microsoft.EntityFrameworkCore;

namespace AiClientHunter.Api.Endpoints;

public static class JobEndpoints
{
    public static void MapJobEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/search-jobs");

        // 1. POST /api/search-jobs (Validate, create, and enqueue search job)
        g.MapPost("", async (SearchJobRequest req, AppDbContext db, JobQueue queue) =>
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(req.City))
                errors["city"] = new[] { "City is required" };

            if (string.IsNullOrWhiteSpace(req.Category))
                errors["category"] = new[] { "Category is required" };

            if (req.Limit is < 1 or > 200)
                errors["limit"] = new[] { "Limit must be between 1 and 200" };

            var leadType = req.LeadType?.Trim().ToLowerInvariant() ?? "";
            if (leadType is not ("hot" or "warm" or "potential"))
                errors["leadType"] = new[] { "Lead type must be 'hot', 'warm', or 'potential'" };

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var job = new SearchJob
            {
                City = req.City.Trim(),
                Category = req.Category.Trim(),
                Source = string.IsNullOrWhiteSpace(req.Source) ? "google" : req.Source.Trim().ToLowerInvariant(),
                LeadType = leadType,
                ResultLimit = req.Limit,
                Status = "queued",
                CreatedAt = DateTime.UtcNow
            };

            db.SearchJobs.Add(job);
            await db.SaveChangesAsync();

            await queue.EnqueueAsync(job.Id);

            return Results.Created($"/api/search-jobs/{job.Id}", job);
        });

        // 2. GET /api/search-jobs (List recent jobs)
        g.MapGet("", async (AppDbContext db) =>
        {
            var rawJobs = await db.SearchJobs
                .AsNoTracking()
                .OrderByDescending(j => j.Id)
                .Take(50)
                .ToListAsync();

            var jobs = rawJobs.Select(j => new
            {
                j.Id,
                j.City,
                j.Category,
                j.Source,
                j.LeadType,
                j.ResultLimit,
                j.Status,
                j.RecordsFound,
                j.NewLeads,
                j.Duplicates,
                j.Error,
                CreatedAt = DateTime.SpecifyKind(j.CreatedAt, DateTimeKind.Utc),
                StartedAt = j.StartedAt.HasValue ? DateTime.SpecifyKind(j.StartedAt.Value, DateTimeKind.Utc) : (DateTime?)null,
                FinishedAt = j.FinishedAt.HasValue ? DateTime.SpecifyKind(j.FinishedAt.Value, DateTimeKind.Utc) : (DateTime?)null
            });

            return Results.Ok(jobs);
        });

        // 3. POST /api/search-jobs/{id:long}/retry (Retry failed jobs safely)
        g.MapPost("{id:long}/retry", async (long id, AppDbContext db, JobQueue queue) =>
        {
            var job = await db.SearchJobs.FindAsync(id);
            if (job is null) return Results.NotFound();

            if (job.Status != "failed")
                return Results.BadRequest(new { error = "Only failed jobs can be retried" });

            job.Status = "queued";
            job.Error = null;
            job.StartedAt = null;
            job.FinishedAt = null;

            await db.SaveChangesAsync();
            await queue.EnqueueAsync(id);

            return Results.Ok(job);
        });
    }
}

public record SearchJobRequest(string City, string Category, string LeadType, int Limit, string? Source = "google");
