using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Fontager.Viewer.Services;

public sealed class UpdateCheckService
{
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private const string GitHubApiUrl = "https://api.github.com/repos/ysfemreAlbyrk/Fontager/releases/latest";

    public UpdateCheckService(SettingsService settings)
    {
        _settings = settings;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Fontager-Viewer", "1.0"));
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceCheck = false)
    {
        // 1. If not forcing a check, see if we've checked in the last 24 hours.
        if (!forceCheck)
        {
            var lastCheck = _settings.LastUpdateCheckTime;
            if (DateTime.UtcNow - lastCheck < TimeSpan.FromDays(1))
            {
                // Return cached results if available
                if (!string.IsNullOrEmpty(_settings.LatestAvailableVersion))
                {
                    var isNew = IsNewerThanCurrent(_settings.LatestAvailableVersion);
                    return new UpdateCheckResult(
                        isNew,
                        _settings.LatestAvailableVersion,
                        _settings.LatestReleaseUrl,
                        "Checked recently (cached result)"
                    );
                }
            }
        }

        try
        {
            var response = await _httpClient.GetStringAsync(GitHubApiUrl);
            if (string.IsNullOrWhiteSpace(response))
                return UpdateCheckResult.NoUpdate;

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp))
                return UpdateCheckResult.NoUpdate;

            var tag = tagProp.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tag))
                return UpdateCheckResult.NoUpdate;

            var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;

            var cleanTag = tag.TrimStart('v', 'V');
            var isAvailable = IsNewerThanCurrent(cleanTag);

            // Persist the state in settings
            _settings.LastUpdateCheckTime = DateTime.UtcNow;
            _settings.LatestAvailableVersion = cleanTag;
            _settings.LatestReleaseUrl = htmlUrl;

            return new UpdateCheckResult(isAvailable, cleanTag, htmlUrl, body);
        }
        catch (Exception ex)
        {
            // Network failure or similar: fail gracefully and don't crash
            return new UpdateCheckResult(false, string.Empty, string.Empty, $"Update check failed: {ex.Message}");
        }
    }

    private static bool IsNewerThanCurrent(string latestVersionStr)
    {
        if (string.IsNullOrWhiteSpace(latestVersionStr)) return false;

        try
        {
            if (!Version.TryParse(latestVersionStr, out var latestVersion))
                return false;

            var currentVersionStr = GetCurrentVersionString();
            if (!Version.TryParse(currentVersionStr, out var currentVersion))
                return false;

            return latestVersion > currentVersion;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentVersionString()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return asm != null ? $"{asm.Major}.{asm.Minor}.{asm.Build}" : "0.0.0";
    }
}

public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; }
    public string LatestVersion { get; }
    public string ReleaseUrl { get; }
    public string ReleaseNotes { get; }

    public UpdateCheckResult(bool isUpdateAvailable, string latestVersion, string releaseUrl, string releaseNotes)
    {
        IsUpdateAvailable = isUpdateAvailable;
        LatestVersion = latestVersion;
        ReleaseUrl = releaseUrl;
        ReleaseNotes = releaseNotes;
    }

    public static readonly UpdateCheckResult NoUpdate = new(false, string.Empty, string.Empty, string.Empty);
}
