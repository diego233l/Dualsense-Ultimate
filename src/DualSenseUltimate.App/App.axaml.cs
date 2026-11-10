using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace DualSenseUltimate.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppState.Initialize();
            ApplyTheme(AppState.Runtime.Settings.Theme);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            desktop.MainWindow = window;
            bool minimized = (desktop.Args ?? []).Contains("--minimized") || AppState.Runtime.Settings.StartMinimized;
            if (minimized) window.ShowInTaskbar = true; // created hidden; the tray icon restores it
            else window.Show();
            desktop.Exit += (_, _) => AppState.Shutdown();
            if (AppState.Runtime.Settings.CheckUpdates) _ = AppState.CheckUpdateAsync(false);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string theme)
    {
        if (Current != null) Current.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private void TrayOpen(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } w })
        {
            w.Show(); w.WindowState = WindowState.Normal; w.Activate();
        }
    }

    private void TrayExit(object? sender, EventArgs e) => ExitApp();

    public static bool IsExiting { get; private set; }

    public static void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
}
