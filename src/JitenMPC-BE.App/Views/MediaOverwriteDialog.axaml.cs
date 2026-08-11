using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using JitenMpcBe.Models;

namespace JitenMpcBe.Views;

public sealed partial class MediaOverwriteDialog : Window
{
    public MediaOverwriteDialog(ExistingCardMedia existing)
    {
        AvaloniaXamlLoader.Load(this);
        var kinds = new List<string>();
        if (existing.HasImage) kinds.Add("image/clip");
        if (existing.HasAudio) kinds.Add("audio");
        this.FindControl<TextBlock>("MessageText")!.Text = $"This card already has {string.Join(" and ", kinds)} media.";
        this.FindControl<Button>("ReplaceButton")!.Click += (_, _) => Close(MediaOverwriteDecision.Replace);
        this.FindControl<Button>("SkipButton")!.Click += (_, _) => Close(MediaOverwriteDecision.SkipMedia);
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close(MediaOverwriteDecision.Cancel);
    }
}
