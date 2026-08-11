using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using JitenMpcBe.Models;

namespace JitenMpcBe.Views;

public sealed partial class MiningReviewWindow : Window
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);
    private const uint SndAsync = 0x0001, SndFilename = 0x00020000;
    private readonly List<(CheckBox Check, SubtitleCue Cue)> _context = [];

    public MiningReviewWindow(string word, MiningMediaBundle bundle, SubtitleCue currentCue, IReadOnlyList<SubtitleCue> contextCues)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("WordText")!.Text = word;
        var imageBorder = this.FindControl<Border>("ImageBorder")!;
        var audioBorder = this.FindControl<Border>("AudioBorder")!;
        imageBorder.IsVisible = bundle.Image is not null;
        audioBorder.IsVisible = bundle.Audio is not null;
        this.FindControl<NumericUpDown>("ImageTimeBox")!.Value = (decimal)bundle.ImageTime;
        this.FindControl<NumericUpDown>("AudioStartBox")!.Value = (decimal)bundle.AudioStart;
        this.FindControl<NumericUpDown>("AudioEndBox")!.Value = (decimal)bundle.AudioEnd;
        this.FindControl<TextBlock>("ImageStatusText")!.Text = bundle.Image is null ? "" : $"{bundle.Image.FileName} · {bundle.Image.Bytes.Length / 1024d:0} KB";
        this.FindControl<TextBlock>("AudioStatusText")!.Text = bundle.Audio is null ? "" : $"{bundle.Audio.FileName} · {bundle.Audio.Bytes.Length / 1024d:0} KB · {(bundle.AudioEnd-bundle.AudioStart):0.00}s";

        if (!string.IsNullOrWhiteSpace(bundle.PreviewImagePath) && File.Exists(bundle.PreviewImagePath))
        {
            try { this.FindControl<Image>("PreviewImage")!.Source = new Bitmap(bundle.PreviewImagePath); }
            catch { this.FindControl<TextBlock>("ImageStatusText")!.Text += " · preview unavailable"; }
        }

        var panel = this.FindControl<StackPanel>("ContextPanel")!;
        foreach (var cue in contextCues)
        {
            var isCurrent = ReferenceEquals(cue, currentCue) || (Math.Abs(cue.Start-currentCue.Start) < .001 && Math.Abs(cue.End-currentCue.End) < .001);
            var check = new CheckBox { Content = cue.Text.Replace('\n',' '), IsChecked = true, IsEnabled = !isCurrent };
            panel.Children.Add(check); _context.Add((check, cue));
        }

        var play = this.FindControl<Button>("PlayAudioButton")!;
        var stop = this.FindControl<Button>("StopAudioButton")!;
        play.IsEnabled = !string.IsNullOrWhiteSpace(bundle.PreviewAudioPath) && File.Exists(bundle.PreviewAudioPath);
        stop.IsEnabled = play.IsEnabled;
        play.Click += (_, _) => { if (play.IsEnabled) PlaySound(bundle.PreviewAudioPath, IntPtr.Zero, SndAsync | SndFilename); };
        stop.Click += (_, _) => PlaySound(null, IntPtr.Zero, 0);
        Closed += (_, _) => PlaySound(null, IntPtr.Zero, 0);

        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(new MiningReviewResult { Accepted = false });
        this.FindControl<Button>("SaveButton")!.Click += (_, _) =>
        {
            var imageTime = (double)(this.FindControl<NumericUpDown>("ImageTimeBox")!.Value ?? (decimal)bundle.ImageTime);
            var audioStart = (double)(this.FindControl<NumericUpDown>("AudioStartBox")!.Value ?? (decimal)bundle.AudioStart);
            var audioEnd = (double)(this.FindControl<NumericUpDown>("AudioEndBox")!.Value ?? (decimal)bundle.AudioEnd);
            if (audioEnd <= audioStart) audioEnd = audioStart + .1;
            var sentence = string.Join("\n", _context.Where(x => x.Check.IsChecked == true).Select(x => x.Cue.Text));
            Close(new MiningReviewResult { Accepted = true, ImageTime = imageTime, AudioStart = audioStart, AudioEnd = audioEnd, Sentence = sentence });
        };
    }
}
