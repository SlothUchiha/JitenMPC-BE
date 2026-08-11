using JitenMpcBe.Native;

namespace JitenMpcBe.Services;

public sealed class KeybindService
{
    private readonly Dictionary<string, bool> _wasDown = new(StringComparer.OrdinalIgnoreCase);

    public bool Pressed(string? binding)
    {
        if (!TryParse(binding, out var key, out var ctrl, out var alt, out var shift)) return false;
        var down = WindowUtil.IsKeyDown(key) && ModifierDown(ctrl, 0x11) && ModifierDown(alt, 0x12) && ModifierDown(shift, 0x10)
                   && (!ctrl || WindowUtil.IsKeyDown(0x11)) && (!alt || WindowUtil.IsKeyDown(0x12)) && (!shift || WindowUtil.IsKeyDown(0x10));
        var id = Normalize(binding!);
        var prior = _wasDown.TryGetValue(id, out var v) && v;
        _wasDown[id] = down;
        return down && !prior;
    }

    public bool MouseLeftPressed()
    {
        const string id = "__mouse_left";
        var down = WindowUtil.IsKeyDown(0x01);
        var prior = _wasDown.TryGetValue(id, out var v) && v;
        _wasDown[id] = down;
        return down && !prior;
    }

    private static bool ModifierDown(bool required, int vk) => required || !WindowUtil.IsKeyDown(vk);

    private static string Normalize(string s) => s.Trim().Replace(" ", "").ToUpperInvariant();

    public static bool TryParse(string? binding, out int virtualKey, out bool ctrl, out bool alt, out bool shift)
    {
        virtualKey = 0; ctrl = alt = shift = false;
        if (string.IsNullOrWhiteSpace(binding)) return false;
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var raw in parts)
        {
            var p = raw.ToUpperInvariant();
            if (p is "CTRL" or "CONTROL") { ctrl = true; continue; }
            if (p == "ALT") { alt = true; continue; }
            if (p == "SHIFT") { shift = true; continue; }
            if (!TryKey(p, out virtualKey)) return false;
        }
        return virtualKey != 0;
    }

    private static bool TryKey(string p, out int vk)
    {
        vk = 0;
        if (p.Length == 1)
        {
            var c = p[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }
        if (p.StartsWith('F') && int.TryParse(p[1..], out var f) && f is >= 1 and <= 24) { vk = 0x70 + f - 1; return true; }
        vk = p switch
        {
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "SPACE" => 0x20, "ENTER" or "RETURN" => 0x0D, "ESC" or "ESCAPE" => 0x1B,
            "HOME" => 0x24, "END" => 0x23, "PGUP" or "PAGEUP" => 0x21, "PGDN" or "PAGEDOWN" => 0x22,
            "TAB" => 0x09, "BACKSPACE" => 0x08, "DELETE" or "DEL" => 0x2E, "INSERT" or "INS" => 0x2D,
            _ => 0
        };
        return vk != 0;
    }
}
