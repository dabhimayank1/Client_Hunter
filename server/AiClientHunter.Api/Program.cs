using AiClientHunter.Api.Ai;
using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Endpoints;
using AiClientHunter.Api.Jobs;
using AiClientHunter.Api.Services;
using AiClientHunter.Api.Sources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Cloud environment: Bind to 0.0.0.0 using PORT env variable (Render / Docker / VPS)
var envPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(envPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{envPort}");
}

// Configure JSON serialization to ignore circular references
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// CORS: Read from config section Cors:AllowedOrigins (env Cors__AllowedOrigins__0 etc.)
// An empty list means "no cross-origin allowed" since the React build is served from the same origin in production.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.Trim().TrimEnd('/'))
    .ToArray() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// Rate limiting: fixed window, 5 attempts per minute per IP for login
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many login attempts. Please try again later." }, token);
    };
    options.AddPolicy("login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// JWT Secret: Read Jwt:Secret from configuration (env var Jwt__Secret).
// If missing or shorter than 32 characters, throw on startup in Production; in Development use clearly-labelled dev-only value.
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Jwt:Secret configuration is missing or shorter than 32 characters in Production environment. Please set the Jwt__Secret environment variable.");
    }

    jwtSecret = $"dev-only-generated-secret-{Guid.NewGuid():N}{Guid.NewGuid():N}";
    builder.Configuration["Jwt:Secret"] = jwtSecret;
}

// Register AuthService & JWT Authentication
builder.Services.AddSingleton<AuthService>();
var jwtKey = Encoding.UTF8.GetBytes(jwtSecret);
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AiClientHunter";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AiClientHunter";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey)
        };
    });
builder.Services.AddAuthorization();

// Database registration:
// On Linux / Docker / Render: Always use SQLite by default (portable single-file cloud DB).
// On Windows: Use SQL Server LocalDB by default, or SQLite if configured.
var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
var envUseSqlite = Environment.GetEnvironmentVariable("USE_SQLITE")?.ToLowerInvariant() == "true";
var cs = builder.Configuration.GetConnectionString("Default");

var useSqlite = !isWindows || envUseSqlite || builder.Configuration.GetValue<bool>("UseSqlite") ||
                builder.Configuration["DATABASE_PROVIDER"]?.ToLowerInvariant() == "sqlite";

builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (useSqlite)
    {
        var dbPath = Environment.GetEnvironmentVariable("SQLITE_PATH") ?? 
                     builder.Configuration["SQLITE_PATH"] ?? 
                     Path.Combine(AppContext.BaseDirectory, "clienthunter.db");

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        o.UseSqlite($"Data Source={dbPath}");
    }
    else
    {
        o.UseSqlServer(cs);
    }
});

// Register Phase 5 OpenStreetMap Overpass Source Adapter
builder.Services.AddHttpClient<OsmOverpassAdapter>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(90);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AiClientHunter/0.1 (contact: clienthunter@local.dev)");
});
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<OsmOverpassAdapter>());

// Register Google/Web Search Adapter (No API Key Required - Free Local Web Search)
builder.Services.AddHttpClient<GoogleWebSearchAdapter>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
});
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<GoogleWebSearchAdapter>());

// Register Freelancer.com Source Adapter (Live Project Briefs & Budgets)
builder.Services.AddHttpClient<FreelancerSourceAdapter>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
});
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<FreelancerSourceAdapter>());

// Register Upwork Source Adapter (Headless Chrome Scraper)
builder.Services.AddTransient<UpworkSourceAdapter>();
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<UpworkSourceAdapter>());

// Register Reddit Source Adapter (Headless Scraper for [Hiring] Posts)
builder.Services.AddTransient<RedditSourceAdapter>();
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<RedditSourceAdapter>());

// Register LinkedIn Source Adapter (Headless Scraper for Hiring Roles & Posts)
builder.Services.AddTransient<LinkedInSourceAdapter>();
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<LinkedInSourceAdapter>());

// Register Justdial Source Adapter (Indian Local Businesses & Direct Phone Numbers)
builder.Services.AddTransient<JustdialSourceAdapter>();
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<JustdialSourceAdapter>());

// Register Zauba Corp Source Adapter (Newly Registered Indian Companies & LLPs)
builder.Services.AddTransient<ZaubaCorpSourceAdapter>();
builder.Services.AddTransient<ISourceAdapter>(sp => sp.GetRequiredService<ZaubaCorpSourceAdapter>());

// Register Phase 6 Services (WebsiteChecker & DedupeService)
builder.Services.AddHttpClient<WebsiteChecker>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AiClientHunter-WebCheck/0.1");
});
builder.Services.AddScoped<DedupeService>();
builder.Services.AddSingleton<ContactEnrichmentService>();

// Register Phase 7 AI Provider (Ollama with 3-minute timeout)
builder.Services.AddHttpClient<IAiProvider, OllamaProvider>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(3);
});

// Register Phase 4 Background Job Queue & Worker
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService<JobWorker>();

var app = builder.Build();

app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Serve React production build from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Map Endpoints
app.MapAuthEndpoints();
app.MapLeadEndpoints();
app.MapJobEndpoints();
app.MapDraftEndpoints();

// Initialize Database & Seed sample data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(db, app.Environment.IsDevelopment(), app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not seed database on startup.");
    }
}

if (app.Environment.IsDevelopment())
{

    app.MapPost("/api/dev/db-check", async (AppDbContext db) =>
    {
        var lead = new Lead { Name = "DB check", DedupeKey = "dbcheck|" + Guid.NewGuid() };
        db.Leads.Add(lead);
        await db.SaveChangesAsync(); // create
        var read = await db.Leads.FindAsync(lead.Id); // read
        read!.Status = "needs_review";
        await db.SaveChangesAsync(); // update
        db.Leads.Remove(read);
        await db.SaveChangesAsync(); // delete
        return Results.Ok("create/read/update/delete passed");
    });
}

// Health check tests API, Database connectivity, and current Phase
app.MapGet("/api/health", async (AppDbContext db) =>
{
    bool dbConnected = false;
    try
    {
        dbConnected = await db.Database.CanConnectAsync();
    }
    catch
    {
        dbConnected = false;
    }

    return Results.Ok(new
    {
        app = "AI Client Hunter API",
        status = "ok",
        phase = 10,
        database = dbConnected ? "ok" : "down",
        serverTimeUtc = DateTime.UtcNow
    });
});

app.MapFallbackToFile("index.html");

if (!string.IsNullOrWhiteSpace(envPort))
{
    app.Run($"http://0.0.0.0:{envPort}");
}
else
{
    app.Run();
}
