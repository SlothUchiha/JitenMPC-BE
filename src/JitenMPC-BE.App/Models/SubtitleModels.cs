namespace JitenMpcBe.Models;

public sealed record SubtitleCue(double Start, double End, string Text);

public sealed class SubtitleStreamInfo
{
    public int Index { get; init; }
    public string Codec { get; init; } = "";
    public string Language { get; init; } = "";
    public string Title { get; init; } = "";
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public bool IsText { get; init; }
    public bool IsAuto { get; init; }

    public string Display
    {
        get
        {
            if (IsAuto) return "Auto - external first, then prefer Japanese embedded";
            var bits = new List<string> { $"Stream #{Index}" };
            if (!string.IsNullOrWhiteSpace(Language)) bits.Add(Language);
            if (!string.IsNullOrWhiteSpace(Codec)) bits.Add(Codec);
            if (!string.IsNullOrWhiteSpace(Title)) bits.Add(Title);
            var flags = new List<string>();
            if (IsDefault) flags.Add("default");
            if (IsForced) flags.Add("forced");
            var text = string.Join(" | ", bits);
            if (flags.Count > 0) text += " [" + string.Join(", ", flags) + "]";
            return text;
        }
    }

    public override string ToString() => Display;

    public static SubtitleStreamInfo Auto { get; } = new() { Index = -1, IsAuto = true, IsText = true };
}
