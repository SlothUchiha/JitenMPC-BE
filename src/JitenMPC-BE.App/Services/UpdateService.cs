using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed record UpdateInfo(string Version, string Name, string HtmlUrl, string DownloadUrl, string Body);

public sealed class UpdateService
{
    private readonly HttpClient _http = new();
    private readonly FileLogger _log;
    public UpdateService(FileLogger log)
    {
        _log = log;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JitenMPC-BE", "0.3"));
    }

    public async Task<UpdateInfo?> CheckAsync(AppSettings settings, Version current)
    {
        var repo = (settings.UpdateRepository ?? "").Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')) return null;
        try
        {
            using var resp = await _http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var parsedText = tag.TrimStart('v', 'V').Split('-', '+')[0];
            if (!Version.TryParse(parsedText, out var latest) || latest <= current) return null;
            var html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var download = "";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() ?? "" : "";
                    if (!(an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || an.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))) continue;
                    download = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    if (an.Contains("setup", StringComparison.OrdinalIgnoreCase) || an.Contains("install", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            return new UpdateInfo(latest.ToString(), name, html, download, body);
        }
        catch (Exception ex) { _log.Write("Update check failed: " + ex.Message); return null; }
    }

    public async Task InstallAsync(UpdateInfo info, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            if (!string.IsNullOrWhiteSpace(info.HtmlUrl)) Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true });
            return;
        }
        var ext = Path.GetExtension(new Uri(info.DownloadUrl).AbsolutePath);
        var path = Path.Combine(dataDirectory, "update" + (string.IsNullOrWhiteSpace(ext) ? ".exe" : ext));
        var bytes = await _http.GetByteArrayAsync(info.DownloadUrl);
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
