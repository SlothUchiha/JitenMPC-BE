using System.Text;

namespace JitenMpcBe.Text;

/// <summary>A base-text/ruby pair parsed from Jiten's bracketed reading notation.</summary>
public sealed record FuriganaSegment(string Text, string Ruby);

/// <summary>
/// Parses readings returned by Jiten such as 一[いち]番[ばん], 逃[に]げる and 食[た]べ物[もの].
/// </summary>
public static class FuriganaParser
{
    /// <summary>
    /// Returns ruby segments only when the annotated bases reconstruct the supplied spelling.
    /// Otherwise callers should fall back to a plain kana reading.
    /// </summary>
    public static IReadOnlyList<FuriganaSegment>? ForSpelling(string spelling, string reading)
    {
        var segments = Parse(reading);
        if (!segments.Any(s => s.Ruby.Length > 0)) return null;
        return string.Concat(segments.Select(s => s.Text)) == spelling ? segments : null;
    }

    /// <summary>Converts Jiten's bracketed reading notation to a plain reading.</summary>
    public static string ToKana(string reading)
        => string.Concat(Parse(reading).Select(s => s.Ruby.Length > 0 ? s.Ruby : s.Text));

    public static List<FuriganaSegment> Parse(string reading)
    {
        var segments = new List<FuriganaSegment>();
        var plain = new StringBuilder();

        for (var i = 0; i < reading.Length; i++)
        {
            var group = reading[i] == '[' ? FindReading(reading, i) : null;
            if (group is null)
            {
                plain.Append(reading[i]);
                continue;
            }

            var (ruby, close) = group.Value;
            var pending = plain.ToString();
            plain.Clear();
            i = close;

            // Jiten annotates the trailing kanji run. Kana before it is okurigana and stays
            // unannotated while sharing the same ruby line height in the popup.
            var runStart = pending.Length;
            while (runStart > 0 && IsRubyBase(pending[runStart - 1])) runStart--;
            if (runStart > 0)
                segments.Add(new FuriganaSegment(pending[..runStart], string.Empty));

            var baseText = pending[runStart..];
            segments.Add(baseText.Length > 0
                ? new FuriganaSegment(baseText, ruby)
                : new FuriganaSegment(ruby, string.Empty));
        }

        if (plain.Length > 0)
            segments.Add(new FuriganaSegment(plain.ToString(), string.Empty));

        return segments;
    }

    private static (string Ruby, int Close)? FindReading(string reading, int open)
    {
        var close = reading.IndexOf(']', open + 1);
        if (close < 0) return null;

        var ruby = reading[(open + 1)..close];
        return ruby.Length > 0 && ruby.All(IsKana) ? (ruby, close) : null;
    }

    public static bool IsKana(char c)
        => c is (>= '\u3040' and <= '\u309F') or (>= '\u30A0' and <= '\u30FF');

    private static bool IsRubyBase(char c)
        => c is (>= '\u4E00' and <= '\u9FFF') or (>= '\u3400' and <= '\u4DBF') or (>= '\u8C48' and <= '\uFAFF')
            or (>= '\uFF10' and <= '\uFF5A') or '々' or 'ヵ' or 'ヶ';
}
