using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace VoidUI.Installer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Uninstall.exe is the slim uninstaller dropped by the wizard (or, on
        // legacy installs, a hard link to the full installer). Running it
        // directly (double-click, no arguments) must start the uninstaller,
        // not the wizard.
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? string.Empty;
        if (exeName.Equals(Product.UninstallExe, StringComparison.OrdinalIgnoreCase) &&
            !args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            args = args.Concat(new[] { "--uninstall" }).ToArray();
        }

        if (args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)))
        {
            try { return RunSilent(args).GetAwaiter().GetResult(); }
            catch { return 1; }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static async Task<int> RunSilent(string[] args)
    {
        bool uninstall = args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));
        bool removeData = args.Any(a => string.Equals(a, "--remove-data", StringComparison.OrdinalIgnoreCase));
        bool noDesktop = args.Any(a => string.Equals(a, "--no-desktop", StringComparison.OrdinalIgnoreCase));
        bool noStartMenu = args.Any(a => string.Equals(a, "--no-startmenu", StringComparison.OrdinalIgnoreCase));
        bool noPin = args.Any(a => string.Equals(a, "--no-pin", StringComparison.OrdinalIgnoreCase));
        bool noTracker = args.Any(a => string.Equals(a, "--no-tracker", StringComparison.OrdinalIgnoreCase));
        bool noGraphics = args.Any(a => string.Equals(a, "--no-graphics", StringComparison.OrdinalIgnoreCase));
        bool noBinds = args.Any(a => string.Equals(a, "--no-binds", StringComparison.OrdinalIgnoreCase));
        bool launch = args.Any(a => string.Equals(a, "--launch", StringComparison.OrdinalIgnoreCase));

        string? dir = ReadArg(args, "--dir");
        string target = !string.IsNullOrWhiteSpace(dir)
            ? System.IO.Path.GetFullPath(dir!)
            : uninstall ? (ReadInstalledDir() ?? Product.DefaultInstallDir) : Product.DefaultInstallDir;

        var progress = new Progress<ProgressInfo>(p => Console.WriteLine($"{p.Percent}% {p.Detail}"));

        if (uninstall)
            await InstallService.UninstallAsync(target, removeData, progress, CancellationToken.None);
        else
            await InstallService.InstallAsync(
                new InstallOptions(target, !noDesktop, !noStartMenu, !noPin,
                    !noTracker, !noGraphics, !noBinds), progress, CancellationToken.None);

        if (launch) InstallService.LaunchApp(target);

        return 0;
    }

    private static string? ReadArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i].Substring(name.Length + 1);
        }
        return null;
    }

    private static string? ReadInstalledDir()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Product.RegistryKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
