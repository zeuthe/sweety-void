using System;
using Avalonia;
using VoidUI.Services;

namespace VoidUI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Old downloaded update installers (~120 MB each) are not needed anymore
        // once the app runs - drop them before anything else.
        UpdateService.PurgeStaleInstallers();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
