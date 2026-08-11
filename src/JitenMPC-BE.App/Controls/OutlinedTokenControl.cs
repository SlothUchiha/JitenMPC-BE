using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JitenMpcBe.Models;

namespace JitenMpcBe.Controls;

public sealed class OutlinedTokenControl : Control
{
    private FormattedText? _formatted;
    private WordStyle _style = new("#EEEEEE", "#000000");
    private TokenVisualOptions _visual = new();
    private double _border;
    private bool _revealed;

    public string Surface { get; private set; } = "";
    public JitenWord? Word { get; private set; }
    public JitenToken? Token { get; private set; }
    public bool Interactive => Word is not null;
    public bool IsBlurred => _visual.Blur && !_revealed;

    public void Configure(string text, JitenWord? word, JitenToken? token, string fontFamily,
        double fontSize, double globalBorder, WordStyle style, TokenVisualOptions? visual = null)
    {
        Surface = text;
        Word = word;
        Token = token;
        _visual = visual ?? new TokenVisualOptions();
        _style = string.IsNullOrWhiteSpace(_visual.PitchColor) || _visual.PitchUnderline
            ? style
            : style with { Text = _visual.PitchColor };
        _border = globalBorder <= 0 ? 0 : _style.OutlineSize * (globalBorder / 3.0);
        var typeface = new Typeface(new FontFamily(fontFamily), _style.Italic ? FontStyle.Italic : FontStyle.Normal,
            _style.Bold ? FontWeight.Bold : FontWeight.Normal, FontStretch.Normal);
        var textBrush = new SolidColorBrush(Color.Parse(_style.Text), _style.Opacity);
        _formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, textBrush);
        ClipToBounds = false;
        UpdateBlur();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetHoverReveal(bool revealed)
    {
        if (_revealed == revealed) return;
        _revealed = revealed;
        UpdateBlur();
    }

    private void UpdateBlur()
    {
        Effect = _visual.Blur && !_revealed ? new BlurEffect { Radius = Math.Max(0.1, _visual.BlurStrength) } : null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_formatted is null) return default;
        return new Size(Math.Max(0, _formatted.Width), Math.Max(0, _formatted.Height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_formatted is null || Surface.Length == 0) return;
        var geometry = _formatted.BuildGeometry(new Point(0, 0));

        if (!string.IsNullOrWhiteSpace(_style.ShadowColor) && _style.ShadowDepth > 0)
        {
            var shadowGeometry = _formatted.BuildGeometry(new Point(_style.ShadowDepth, _style.ShadowDepth));
            if (shadowGeometry is not null)
                context.DrawGeometry(new SolidColorBrush(Color.Parse(_style.ShadowColor), _style.Opacity), null, shadowGeometry);
        }

        var outlineColor = _visual.IPlusOneHighlight ? "#FBBF24" : _style.Outline;
        var outlineWidth = _visual.IPlusOneHighlight ? Math.Max(_border, 2.5) : _border;
        if (outlineWidth > 0 && geometry is not null)
        {
            var outline = new SolidColorBrush(Color.Parse(outlineColor), _style.Opacity);
            var pen = new Pen(outline, outlineWidth * 2.0, null, PenLineCap.Round, PenLineJoin.Round);
            context.DrawGeometry(null, pen, geometry);
        }
        context.DrawText(_formatted, new Point(0, 0));

        var y = Math.Max(0, Bounds.Height - 1.5);
        if (_style.Underline)
        {
            var c = string.IsNullOrWhiteSpace(_style.UnderlineColor) ? _style.Text : _style.UnderlineColor;
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse(c), _style.Opacity), Math.Max(1, _style.UnderlineThickness)), new Point(0, y), new Point(Bounds.Width, y));
        }
        if (_visual.FrequencyUnderline)
        {
            var fy = Math.Max(0, y - (_style.Underline ? Math.Max(2, _style.UnderlineThickness + 1) : 0));
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#A78BFA"), .95), 2), new Point(0, fy), new Point(Bounds.Width, fy));
        }
        if (_visual.PitchUnderline && !string.IsNullOrWhiteSpace(_visual.PitchColor))
        {
            var py = Math.Max(0, y - ((_style.Underline ? _style.UnderlineThickness + 1 : 0) + (_visual.FrequencyUnderline ? 3 : 0)));
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse(_visual.PitchColor), .98), Math.Max(1, _visual.PitchUnderlineThickness)), new Point(0, py), new Point(Bounds.Width, py));
        }
        if (_style.Strikethrough)
        {
            var sy = Bounds.Height * .50;
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse(_style.Text), _style.Opacity), Math.Max(1, _style.UnderlineThickness)), new Point(0, sy), new Point(Bounds.Width, sy));
        }
        if (_visual.DebugHitbox)
        {
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#FF00FF"), .8), 1), new Rect(0, 0, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1)));
        }
    }
}
