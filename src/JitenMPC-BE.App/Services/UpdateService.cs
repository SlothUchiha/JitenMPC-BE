using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed record UpdateInfo(
    string Version,
    string Name,
    string HtmlUrl,
    string DownloadUrl,
    string AssetName,
    string Body)
{
    public bool CanInstall => !string.IsNullOrWhiteSpace(DownloadUrl)
        && !string.IsNullOrWhiteSpace(AssetName)
        && AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}

public sealed record UpdateCheckResult(bool Succeeded, UpdateInfo? Update, string Error = "");

public sealed class UpdateService
{
    public const string DefaultRepository = "SlothUchiha/JitenMPC-BE";

    private readonly HttpClient _http = new();
    private readonly FileLogger _log;

    public UpdateService(FileLogger log)
    {
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JitenMPC-BE", "0.4"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckAsync(AppSettings settings, string currentVersionText)
    {
        var repo = (settings.UpdateRepository ?? "").Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
            return new UpdateCheckResult(false, null, "The update repository is not configured.");

        if (!SemanticVersion.TryParse(currentVersionText, out var current))
            return new UpdateCheckResult(false, null, $"Could not read the current application version ({currentVersionText}).");

        try
        {
            // /releases/latest excludes prereleases, so use the collection endpoint. Preview
            // builds can then see newer previews while stable builds deliberately ignore them.
            using var resp = await _http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=30");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub returned an unexpected releases response.");

            UpdateInfo? bestInfo = null;
            SemanticVersion? bestVersion = null;
            var includePrereleases = current.IsPrerelease;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;

                var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                if (!SemanticVersion.TryParse(tag, out var candidate))
                    continue;

                var markedPrerelease = release.TryGetProperty("prerelease", out var prereleaseEl)
                    && prereleaseEl.ValueKind == JsonValueKind.True;
                if ((candidate.IsPrerelease || markedPrerelease) && !includePrereleases)
                    continue;
                if (candidate.CompareTo(current) <= 0)
                    continue;
                if (bestVersion is not null && candidate.CompareTo(bestVersion) <= 0)
                    continue;

                var html = release.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() ?? "" : "";
                var name = release.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? tag : tag;
                var body = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                FindInstallerAsset(release, candidate.ToString(), out var downloadUrl, out var assetName);

                bestVersion = candidate;
                bestInfo = new UpdateInfo(candidate.ToString(), name, html, downloadUrl, assetName, body);
            }

            // A successful "already current" response is still a completed check.
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            return new UpdateCheckResult(true, bestInfo);
        }
        catch (Exception ex)
        {
            _log.Write("Update check failed: " + ex.Message);
            return new UpdateCheckResult(false, null, ex.Message);
        }
    }

    public async Task<string> DownloadInstallerAsync(UpdateInfo info)
    {
        if (!info.CanInstall)
            throw new InvalidOperationException("This release does not contain a JitenMPC-BE installer asset.");

        var fileName = Path.GetFileName(info.AssetName);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.StartsWith("JitenMPC-BE-Setup-v", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The release installer has an unexpected filename.");

        var directory = Path.Combine(Path.GetTempPath(), "JitenMPC-BE", "Updates", info.Version);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);

        using var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
        {
            await input.CopyToAsync(output);
        }

        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new IOException("The downloaded update installer is empty.");

        return path;
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("The downloaded update installer could not be found.", installerPath);

        // /SILENT keeps a small progress window and real errors visible. Jiten exits immediately
        // after this starts; the Inno [Run] entry launches the upgraded application afterward.
        var process = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = "/SILENT /SP- /NORESTART /CLOSEAPPLICATIONS"
        });
        if (process is null)
            throw new InvalidOperationException("Windows could not start the update installer.");
    }

    public void OpenRelease(UpdateInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.HtmlUrl))
            throw new InvalidOperationException("The update does not have a GitHub release page.");
        Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
    }

    private static void FindInstallerAsset(JsonElement release, string version, out string downloadUrl, out string assetName)
    {
        downloadUrl = "";
        assetName = "";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return;

        var expected = $"JitenMPC-BE-Setup-v{version}.exe";
        var found = false;
        JsonElement selected = default;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (name.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                selected = asset;
                found = true;
                break;
            }

            if (!found
                && name.StartsWith("JitenMPC-BE-Setup-", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                selected = asset;
                found = true;
            }
        }

        if (!found) return;
        assetName = selected.TryGetProperty("name", out var selectedName) ? selectedName.GetString() ?? "" : "";
        downloadUrl = selected.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
    }

    private sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch, string[] prerelease)
        {
            Major = major; Minor = minor; Patch = patch; Prerelease = prerelease;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string[] Prerelease { get; }
        public bool IsPrerelease => Prerelease.Length > 0;

        public static bool TryParse(string? text, out SemanticVersion version)
        {
            version = null!;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var value = text.Trim().TrimStart('v', 'V');
            var plus = value.IndexOf('+');
            if (plus >= 0) value = value[..plus];

            string prereleaseText = "";
            var dash = value.IndexOf('-');
            if (dash >= 0)
            {
                prereleaseText = value[(dash + 1)..];
                value = value[..dash];
            }

            var core = value.Split('.');
            if (core.Length is < 1 or > 4) return false;
            if (!int.TryParse(core[0], out var major)) return false;

            var minor = 0;
            if (core.Length > 1 && !int.TryParse(core[1], out minor)) return false;

            var patch = 0;
            if (core.Length > 2 && !int.TryParse(core[2], out patch)) return false;

            if (core.Length > 3 && !int.TryParse(core[3], out _)) return false;

            var prerelease = string.IsNullOrWhiteSpace(prereleaseText)
                ? []
                : prereleaseText.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (!string.IsNullOrWhiteSpace(prereleaseText) && prerelease.Length == 0) return false;

            version = new SemanticVersion(major, minor, patch, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            var core = Major.CompareTo(other.Major);
            if (core != 0) return core;
            core = Minor.CompareTo(other.Minor);
            if (core != 0) return core;
            core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;

            if (!IsPrerelease && !other.IsPrerelease) return 0;
            if (!IsPrerelease) return 1;
            if (!other.IsPrerelease) return -1;

            var count = Math.Min(Prerelease.Length, other.Prerelease.Length);
            for (var i = 0; i < count; i++)
            {
                var left = Prerelease[i];
                var right = other.Prerelease[i];
                var leftNumeric = int.TryParse(left, out var leftNumber);
                var rightNumeric = int.TryParse(right, out var rightNumber);
                int comparison;
                if (leftNumeric && rightNumeric) comparison = leftNumber.CompareTo(rightNumber);
                else if (leftNumeric) comparison = -1;
                else if (rightNumeric) comparison = 1;
                else comparison = string.Compare(left, right, StringComparison.Ordinal);
                if (comparison != 0) return comparison;
            }
            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }

        public override string ToString()
        {
            var core = $"{Major}.{Minor}.{Patch}";
            return IsPrerelease ? core + "-" + string.Join(".", Prerelease) : core;
        }
    }
}
