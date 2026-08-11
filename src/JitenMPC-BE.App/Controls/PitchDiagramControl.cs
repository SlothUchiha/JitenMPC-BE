using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using JitenMpcBe.Text;

namespace JitenMpcBe.Controls;

/// <summary>
/// Compact mora/contour pitch-accent diagram: filled dots are the word's morae and the final
/// hollow dot is the following particle, matching the visual convention used by JitenMPV.
/// </summary>
public sealed class PitchDiagramControl : Control
{
    public PitchDiagram? Diagram { get; init; }
    public string AccentColor { get; init; } = "#C4B5FD";
    public double ScaleFactor { get; init; } = 1.0;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Diagram is not { } diagram) return default;
        var scale = Math.Clamp(ScaleFactor, .5, 1.5);
        return new Size(diagram.Pattern.Count * 18 * scale, 34 * scale);
    }

    public override void Render(DrawingContext context)
    {
        if (Diagram is not { } diagram || diagram.Pattern.Count == 0) return;

        var scale = Math.Clamp(ScaleFactor, .5, 1.5);
        var stepX = 18 * scale;
        var padX = 9 * scale;
        var highY = 5 * scale;
        var lowY = 17 * scale;
        var radius = 3 * scale;
        var textOffset = 8 * scale;
        var fontSize = 9 * scale;
        var strokeWidth = 1.5 * scale;

        var color = Color.TryParse(AccentColor, out var parsed) ? parsed : Colors.Gray;
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, strokeWidth);
        var typeface = new Typeface(FontFamily.Default, weight: FontWeight.Bold);
        var points = new Point[diagram.Pattern.Count];

        for (var i = 0; i < diagram.Pattern.Count; i++)
            points[i] = new Point(padX + i * stepX, diagram.Pattern[i] ? highY : lowY);

        for (var i = 1; i < points.Length; i++)
            context.DrawLine(pen, points[i - 1], points[i]);

        for (var i = 0; i < points.Length; i++)
        {
            var isParticle = i == points.Length - 1;
            context.DrawEllipse(isParticle ? Brushes.Transparent : brush, pen, points[i], radius, radius);
            if (isParticle || i >= diagram.Morae.Count) continue;

            var text = new FormattedText(
                diagram.Morae[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fontSize, brush);
            context.DrawText(text, new Point(points[i].X - text.Width / 2, points[i].Y + textOffset));
        }
    }
}
