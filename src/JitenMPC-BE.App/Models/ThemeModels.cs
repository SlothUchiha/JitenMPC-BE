namespace JitenMpcBe.Models;

public sealed record WordStyle(
    string Text,
    string Outline,
    double OutlineSize = 3,
    double Opacity = 1,
    bool Bold = false,
    bool Underline = false,
    string ShadowColor = "",
    double ShadowDepth = 0,
    bool Italic = false,
    bool Strikethrough = false,
    string UnderlineColor = "",
    double UnderlineThickness = 2);

public static class ThemePresets
{
    public static readonly string[] Names = ["Default", "High Contrast", "Monochrome", "Subtle", "Underline", "Toy Box", "Custom"];
    public static readonly string[] StateNames = ["New", "Young", "Mature", "Blacklisted", "Due", "Mastered", "Redundant", "Suspended"];

    private static WordStyle S(string text, string outline, double outlineSize = 3, double opacity = 1,
        bool bold = false, bool underline = false, string shadowColor = "", double shadowDepth = 0,
        bool italic = false, bool strike = false, string underlineColor = "", double underlineThickness = 2)
        => new(text, outline, outlineSize, opacity, bold, underline, shadowColor, shadowDepth, italic, strike, underlineColor, underlineThickness);

    private static readonly Dictionary<string, Dictionary<int, WordStyle>> Themes = Build();

    public static WordStyle For(string themeName, int state, AppSettings? settings = null)
    {
        if (string.Equals(themeName, "Custom", StringComparison.OrdinalIgnoreCase) && settings is not null)
        {
            var c = settings.GetCustomState(Math.Clamp(state, 0, 7));
            return S(c.TextColor, c.OutlineColor, c.OutlineSize, Math.Clamp(c.TextOpacityPercent / 100.0, 0, 1),
                c.Bold, c.Underline, c.HasShadow ? c.ShadowColor : "", c.HasShadow ? c.ShadowDepth : 0,
                c.Italic, c.Strikethrough, c.UnderlineColor, c.UnderlineThickness);
        }
        if (!Themes.TryGetValue(themeName, out var theme)) theme = Themes["Default"];
        return theme.TryGetValue(state, out var style) ? style : S("#EEEEEE", "#000000");
    }

    private static Dictionary<string, Dictionary<int, WordStyle>> Build()
    {
        var dim = S("#969696", "#505050", 3, 150.0 / 255.0);
        var hcDim = S("#808080", "#000000", 3, 120.0 / 255.0);
        var monoDim = S("#EEEEEE", "#000000", 3, 102.0 / 255.0);
        var subtleDim = S("#C0C0C0", "#202020", 2, 180.0 / 255.0);
        var toyDim = S("#888888", "#000000", 3, 130.0 / 255.0);
        return new()
        {
            ["Default"] = new() { [0]=S("#A566EF","#000000"), [1]=S("#EEEEEE","#D08700"), [2]=S("#EEEEEE","#70C000"), [3]=dim, [4]=S("#EEEEEE","#FF4500"), [5]=S("#C8C8C8","#006400",3,200.0/255.0), [6]=dim, [7]=dim },
            ["High Contrast"] = new() { [0]=S("#00FFFF","#000000",4,1,true), [1]=S("#FFFF00","#000000",4,1,false,true,"",0,false,false,"#FFFF00",2), [2]=S("#00FF00","#000000",4), [3]=hcDim, [4]=S("#FF4444","#000000",4,1,true), [5]=S("#88FF88","#000000"), [6]=hcDim, [7]=hcDim },
            ["Monochrome"] = new() { [0]=S("#CCCCCC","#000000"), [1]=S("#999999","#000000"), [2]=S("#666666","#000000"), [3]=monoDim, [4]=S("#FFFFFF","#000000",3,1,false,true,"",0,false,false,"#FFFFFF",2), [5]=S("#EEEEEE","#000000"), [6]=monoDim, [7]=monoDim },
            ["Subtle"] = new() { [0]=S("#E0E0E0","#3A2060",2), [1]=S("#E0E0E0","#4A3500",2), [2]=S("#E0E0E0","#2A4A00",2), [3]=subtleDim, [4]=S("#E0E0E0","#4A1500",2), [5]=S("#C8C8C8","#1A3A00",2), [6]=subtleDim, [7]=subtleDim },
            ["Underline"] = new() { [0]=S("#EEEEEE","#000000",3,1,false,true,"",0,false,false,"#A566EF",2), [1]=S("#EEEEEE","#000000",3,1,false,true,"",0,false,false,"#E8A020",2), [2]=S("#EEEEEE","#000000"), [3]=S("#EEEEEE","#000000"), [4]=S("#EEEEEE","#000000",3,1,false,true,"",0,false,false,"#E03030",2), [5]=S("#EEEEEE","#000000"), [6]=S("#EEEEEE","#000000"), [7]=S("#EEEEEE","#000000") },
            ["Toy Box"] = new() { [0]=S("#4B8DFF","#000000"), [1]=S("#55B87A","#000000",3,1,false,true,"",0,false,false,"#55B87A",2), [2]=S("#EEEEEE","#000000"), [3]=toyDim, [4]=S("#D08700","#000000",3,1,false,true,"",0,false,false,"#D08700",2), [5]=S("#AAAAAA","#000000"), [6]=toyDim, [7]=toyDim }
        };
    }
}

public sealed record TokenVisualOptions(
    bool FrequencyUnderline = false,
    bool IPlusOneHighlight = false,
    bool Blur = false,
    double BlurStrength = 6,
    string PitchColor = "",
    bool PitchUnderline = false,
    double PitchUnderlineThickness = 4,
    bool DebugHitbox = false);
