using System.Net;
using System.Net.Sockets;

namespace AiClientHunter.Api.Services;

public record WebsiteCheckResult(string Status, string? Url, string Note);

public class WebsiteChecker(HttpClient http, ILogger<WebsiteChecker> log)
{
    private static readonly SemaphoreSlim Throttler = new(5, 5);

    // The listing already has a URL: does it respond?
    public async Task<WebsiteCheckResult> CheckListedAsync(string url, CancellationToken ct)
    {
        await Throttler.WaitAsync(ct);
        try
        {
            log.LogDebug("Checking listed website: {Url}", url);
            var target = url.Contains("://") ? url : "http://" + url;
            using var resp = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode < 400
                ? new("verified", target, $"HTTP {(int)resp.StatusCode}")
                : new("unknown", target, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            // Could be temporary network glitch or server blocking crawlers, so label honest 'unknown' instead of 'not_found'
            return new("unknown", url, ex.GetType().Name);
        }
        finally
        {
            Throttler.Release();
        }
    }

    // No URL in the listing: probe candidate domains via lightweight DNS resolution only
    public async Task<WebsiteCheckResult> FindCandidateAsync(string businessName, CancellationToken ct)
    {
        var slug = new string(businessName.ToLowerInvariant()
            .Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).ToArray());

        if (slug.Length < 4)
            return new("unknown", null, "Name too short or not in Latin letters");

        foreach (var host in new[] { slug + ".com", slug + ".in", slug + ".co.in" })
        {
            try
            {
                using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                dnsCts.CancelAfter(TimeSpan.FromSeconds(2));
                var ips = await Dns.GetHostAddressesAsync(host, dnsCts.Token);
                if (ips.Length > 0)
                {
                    return new("unknown", "https://" + host,
                        "Domain exists. Confirm manually it belongs to this business.");
                }
            }
            catch (SocketException) { /* does not resolve, check next extension */ }
            catch (OperationCanceledException) { /* DNS probe timed out, continue */ }
            catch (Exception) { /* timeout/error, continue */ }
        }

        return new("not_found", null,
            "No likely domain resolved. This is not proof that no website exists.");
    }
}
