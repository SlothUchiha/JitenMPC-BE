using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JitenMpcBe.Services;
using JitenMpcBe.Views;

namespace JitenMpcBe;

public sealed partial class App : Application
{
    public AppRuntime? Runtime { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Runtime = new AppRuntime();
            var main = new MainWindow(Runtime);
            Runtime.AttachMainWindow(main);
            desktop.MainWindow = main;
            desktop.Exit += (_, _) => Runtime.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
