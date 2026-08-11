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
            var runtime = new AppRuntime();
            Runtime = runtime;
            var main = new MainWindow(runtime);
            runtime.AttachMainWindow(main);

            // The subtitle overlay and dictionary popup are real Avalonia windows. With the
            // default OnLastWindowClose lifetime they can keep the process alive after the
            // main/settings window is closed. The main window is the application's lifetime
            // owner: Close Settings may still Hide() it, but actually closing it exits Jiten.
            desktop.MainWindow = main;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            runtime.ApplicationExitRequested += () => desktop.Shutdown();
            desktop.Exit += (_, _) => runtime.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
