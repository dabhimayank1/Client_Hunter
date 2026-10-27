namespace AiClientHunter.Api.Sources;

public static class ScraperPathHelper
{
    public static string GetScraperDirectory()
    {
        var env = Environment.GetEnvironmentVariable("SCRAPER_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scraper"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scraper")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../scraper")),
            @"/app/scraper",
            @"c:\Users\DELL\Desktop\client hunter\scraper"
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
                return dir;
        }

        return AppContext.BaseDirectory;
    }
}
