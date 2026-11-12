using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MUTEit.Core.Updates;

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,

    /// <summary>The repository has no published release yet.</summary>
    NoReleases,

    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string? LatestVersion = null, string? ReleaseUrl = null, string? Error = null);

/// <summary>
/// Optional check for new releases (off by default): one HTTPS request to the public GitHub API
/// <c>api.github.com/repos/complicatiion/MUTEit/releases/latest</c>. GitHub receives the IP address and the user
/// agent "MUTEit/&lt;version&gt;"; nothing else is sent and nothing is downloaded or installed.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "complicatiion/MUTEit";
    public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    private const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MUTEit", Clean(currentVersion)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.NoReleases, ReleaseUrl: ReleasesPage);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: $"HTTP {(int)response.StatusCode}");
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            var tag = json.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = json.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : ReleasesPage;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateCheckResult(UpdateCheckOutcome.NoReleases, ReleaseUrl: ReleasesPage);
            }

            var latest = Clean(tag);
            return IsNewer(latest, currentVersion)
                ? new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, latest, url)
                : new UpdateCheckResult(UpdateCheckOutcome.UpToDate, latest, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or FormatException)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.Failed, Error: ex.Message);
        }
    }

    /// <summary>"v0.0.6" → "0.0.6"; drops build metadata ("+abc") and pre-release suffixes for the comparison.</summary>
    public static string Clean(string version)
    {
        var v = version.Trim().TrimStart('v', 'V');
        var cut = v.IndexOfAny(['+', ' ']);
        return cut > 0 ? v[..cut] : v;
    }

    public static bool IsNewer(string candidate, string current)
    {
        static Version? Parse(string s)
        {
            var core = Clean(s);
            var dash = core.IndexOf('-');
            if (dash > 0)
            {
                core = core[..dash];
            }

            return Version.TryParse(core, out var v) ? v : null;
        }

        var a = Parse(candidate);
        var b = Parse(current);
        return a is not null && b is not null && a > b;
    }
}
