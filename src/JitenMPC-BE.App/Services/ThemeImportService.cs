using System.IO.Compression;
using System.Text;
using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public static class ThemeImportService
{
    public static bool TryImport(string? code, AppSettings settings, out string status)
    {
        status = "Invalid JitenReader theme share code.";
        if (string.IsNullOrWhiteSpace(code)) return false;
        var payload = code.Trim();
        if (payload.StartsWith("jtr:1", StringComparison.OrdinalIgnoreCase)) payload = payload[5..].TrimStart(':');
        try
        {
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var bytes = Convert.FromBase64String(payload);
            var json = TryDecode(bytes);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Reader share formats have changed over time; recursively accept recognizable state-style objects.
            var imported = 0;
            foreach (var state in ThemePresets.StateNames.Select((name, i) => (name, i)))
            {
                if (!TryFindObject(root, state.name, out var obj) && !TryFindObject(root, state.i.ToString(), out obj)) continue;
                var target = settings.GetCustomState(state.i);
                target.TextColor = ReadColor(obj, "textColor", "text", "color") ?? target.TextColor;
                target.OutlineColor = ReadColor(obj, "outlineColor", "outline") ?? target.OutlineColor;
                target.UnderlineColor = ReadColor(obj, "underlineColor") ?? target.UnderlineColor;
                target.ShadowColor = ReadColor(obj, "shadowColor") ?? target.ShadowColor;
                imported++;
            }
            if (imported == 0) return false;
            settings.Theme = "Custom";
            status = $"Imported {imported} state styles from JitenReader.";
            return true;
        }
        catch { return false; }
    }

    private static string TryDecode(byte[] bytes)
    {
        var plain = Encoding.UTF8.GetString(bytes);
        var trimmed = plain.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return plain;
        using var input = new MemoryStream(bytes);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(z, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool TryFindObject(JsonElement root, string name, out JsonElement found)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object) { found = p.Value; return true; }
                if (TryFindObject(p.Value, name, out found)) return true;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var e in root.EnumerateArray()) if (TryFindObject(e, name, out found)) return true;
        found = default; return false;
    }

    private static string? ReadColor(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) return e.GetString();
        return null;
    }
}
