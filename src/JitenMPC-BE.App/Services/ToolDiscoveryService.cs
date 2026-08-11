using System.Diagnostics;

namespace JitenMpcBe.Services;

public sealed class ToolDiscoveryService
{
    private readonly string _baseDir;
    private readonly FileLogger _log;
    public ToolDiscoveryService(string baseDir, FileLogger log) { _baseDir = baseDir; _log = log; }

    public string FindMpc(string configured)
    {
        if (File.Exists(configured)) return Path.GetFullPath(configured);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(pf, "MPC-BE x64", "mpc-be64.exe"),
            Path.Combine(pf, "MPC-BE", "mpc-be64.exe"),
            Path.Combine(pf86, "MPC-BE", "mpc-be.exe"),
            Path.Combine(local, "MPC-BE", "mpc-be64.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? FindOnPath("mpc-be64.exe") ?? FindOnPath("mpc-be.exe") ?? "";
    }

    public (string ffmpeg, string ffprobe) FindFfmpegPair(string configuredFfmpeg, string configuredFfprobe)
    {
        var ffmpeg = FindTool(configuredFfmpeg, "ffmpeg");
        var ffprobe = FindTool(configuredFfprobe, "ffprobe");
        if (!string.IsNullOrWhiteSpace(ffmpeg) && string.IsNullOrWhiteSpace(ffprobe))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            if (File.Exists(sibling)) ffprobe = sibling;
        }
        if (!string.IsNullOrWhiteSpace(ffprobe) && string.IsNullOrWhiteSpace(ffmpeg))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffprobe)!, "ffmpeg.exe");
            if (File.Exists(sibling)) ffmpeg = sibling;
        }
        if (!string.IsNullOrWhiteSpace(ffmpeg)) _log.Write("Auto-detected ffmpeg: " + ffmpeg);
        if (!string.IsNullOrWhiteSpace(ffprobe)) _log.Write("Auto-detected ffprobe: " + ffprobe);
        return (ffmpeg, ffprobe);
    }

    private string FindTool(string configured, string name)
    {
        var exe = name + ".exe";
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return Path.GetFullPath(configured);
            if (Directory.Exists(configured))
            {
                var fromDir = Path.Combine(configured, exe);
                if (File.Exists(fromDir)) return fromDir;
            }
        }

        foreach (var local in new[]
        {
            Path.Combine(_baseDir, exe), Path.Combine(_baseDir, "bin", exe),
            Path.Combine(_baseDir, "tools", exe), Path.Combine(_baseDir, "ffmpeg", "bin", exe)
        }) if (File.Exists(local)) return local;

        var onPath = FindOnPath(exe);
        if (onPath is not null) return onPath;

        var la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var chocolatey = Environment.GetEnvironmentVariable("ChocolateyInstall") ?? "";
        var candidates = new[]
        {
            Path.Combine(la, "Microsoft", "WinGet", "Links", exe),
            Path.Combine(la, "Programs", "ffmpeg", "bin", exe),
            Path.Combine(profile, "scoop", "shims", exe),
            Path.Combine(profile, "scoop", "apps", "ffmpeg", "current", "bin", exe),
            string.IsNullOrWhiteSpace(chocolatey) ? "" : Path.Combine(chocolatey, "bin", exe),
            Path.Combine(programData, "chocolatey", "bin", exe),
            Path.Combine(pf, "ffmpeg", "bin", exe), Path.Combine(pf, "FFmpeg", "bin", exe),
            Path.Combine(pf86, "ffmpeg", "bin", exe), Path.Combine(pf86, "FFmpeg", "bin", exe)
        };
        foreach (var c in candidates) if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;

        var wingetPackages = Path.Combine(la, "Microsoft", "WinGet", "Packages");
        try
        {
            if (Directory.Exists(wingetPackages))
            {
                foreach (var dir in Directory.EnumerateDirectories(wingetPackages).Where(d => Path.GetFileName(d).Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)))
                {
                    var hit = Directory.EnumerateFiles(dir, exe, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit is not null) return hit;
                }
            }
        }
        catch { }
        return "";
    }

    private static string? FindOnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }
        return null;
    }
}
