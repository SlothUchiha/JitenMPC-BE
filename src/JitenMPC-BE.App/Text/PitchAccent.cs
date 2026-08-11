namespace JitenMpcBe.Text;

public enum PitchClass
{
    Unknown = 0,
    Heiban,
    Atamadaka,
    Nakadaka,
    Odaka
}

/// <param name="Pattern">One point per mora plus a trailing point for the following particle; true is high.</param>
public sealed record PitchDiagram(
    IReadOnlyList<string> Morae,
    IReadOnlyList<bool> Pattern,
    PitchClass Class);

public static class PitchAccent
{
    private static readonly HashSet<char> SmallNonMora =
        ['ゃ', 'ゅ', 'ょ', 'ャ', 'ュ', 'ョ', 'ァ', 'ィ', 'ゥ', 'ェ', 'ォ'];

    public static List<string> SplitMorae(string reading)
    {
        var morae = new List<string>();
        foreach (var ch in reading)
        {
            if (morae.Count > 0 && SmallNonMora.Contains(ch))
                morae[^1] += ch;
            else
                morae.Add(ch.ToString());
        }
        return morae;
    }

    public static string CleanReading(string reading)
        => new string(FuriganaParser.ToKana(reading).Where(FuriganaParser.IsKana).ToArray());

    public static PitchClass Classify(int accent, int moraCount)
    {
        if (moraCount <= 0 || accent < 0) return PitchClass.Unknown;
        if (accent == 0) return PitchClass.Heiban;
        if (accent == moraCount) return PitchClass.Odaka;
        if (accent == 1) return PitchClass.Atamadaka;
        if (accent < moraCount) return PitchClass.Nakadaka;
        return PitchClass.Unknown;
    }

    /// <summary>Accent 0 is heiban; otherwise the number is the mora after which pitch drops.</summary>
    public static PitchDiagram? BuildDiagram(string reading, int accent)
    {
        var morae = SplitMorae(CleanReading(reading));
        if (morae.Count == 0 || accent < 0 || accent > morae.Count) return null;

        var pattern = new bool[morae.Count + 1];
        if (accent == 0)
        {
            for (var i = 1; i <= morae.Count; i++)
                pattern[i] = true;
        }
        else
        {
            pattern[0] = accent == 1;
            for (var i = 1; i < morae.Count; i++)
                pattern[i] = i < accent;
        }

        return new PitchDiagram(morae, pattern, Classify(accent, morae.Count));
    }

    public static string DefaultColor(PitchClass pitchClass) => pitchClass switch
    {
        PitchClass.Heiban => "#D20CA3",
        PitchClass.Atamadaka => "#EA9316",
        PitchClass.Nakadaka => "#27A2FF",
        PitchClass.Odaka => "#0CD24D",
        _ => "#C4B5FD"
    };
}
