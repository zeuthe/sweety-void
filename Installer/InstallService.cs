using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoidUI.Installer;

internal static class Product
{
    public const string Name = "Sweety Void";

    // The build passes -p:Version, so the assembly version is the source of
    // truth; the old hardcoded constant went stale and reported 1.0.0.
    public static string Version
    {
        get
        {
            try
            {
                var v = typeof(Product).Assembly.GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch { return "1.0.0"; }
        }
    }

    public const string Publisher = "Sweety";
    public const string AppExe = "VoidUI.exe";
    public const string UninstallExe = "Uninstall.exe";
    public const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SweetyVoid";
    public const string PayloadResource = "payload.zip";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Sweety Void");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk");

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk");

    public static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI");
}

internal sealed record InstallOptions(
    string TargetDir,
    bool DesktopShortcut,
    bool StartMenuShortcut,
    bool PinTaskbar,



    bool Tracker = true,
    bool Graphics = true,
    bool Binds = true);

internal sealed record ProgressInfo(int Percent, string Detail);

internal static class InstallService
{
    // ───────────────────────────── Install ─────────────────────────────

    public static async Task InstallAsync(InstallOptions options, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        var target = options.TargetDir;
        Directory.CreateDirectory(target);

        await ExtractPayloadAsync(target, progress, ct);


        progress.Report(new ProgressInfo(91, "Writing the feature list..."));
        WriteFeatures(target, options);

        progress.Report(new ProgressInfo(92, "Creating shortcuts..."));
        CreateShortcuts(target, options);
        if (options.PinTaskbar)
        {
            progress.Report(new ProgressInfo(95, "Pinning to taskbar..."));
            PinToTaskbar(target);
        }

        progress.Report(new ProgressInfo(97, "Registering the uninstaller..."));
        CreateUninstaller(target);
        WriteUninstallRegistry(target);


        WriteInstallLog(target, options);

        progress.Report(new ProgressInfo(100, "Done"));
    }


    private static void WriteFeatures(string target, InstallOptions options)
    {
        try
        {
            var json = "{\n" +
                       $"  \"Tracker\": {(options.Tracker ? "true" : "false")},\n" +
                       $"  \"Graphics\": {(options.Graphics ? "true" : "false")},\n" +
                       $"  \"Binds\": {(options.Binds ? "true" : "false")}\n" +
                       "}\n";
            File.WriteAllText(Path.Combine(target, "features.json"), json);
        }
        catch {  }
    }


    private static void WriteInstallLog(string target, InstallOptions options)
    {
        try
        {
            var files = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;
            var sizeMb = Math.Round(DirSize(target) / 1024d / 1024d, 1);

            var features = new[]
            {
                options.Tracker ? "tracker" : null,
                options.Graphics ? "graphics" : null,
                options.Binds ? "binds" : null
            }.Where(f => f != null).ToArray();
            if (features.Length == 0) features = new[] { "home+crosshair" };

            var shortcuts = new[]
            {
                options.DesktopShortcut ? "desktop" : null,
                options.StartMenuShortcut ? "start-menu" : null,
                options.PinTaskbar ? "taskbar" : null
            }.Where(s => s != null).ToArray();

            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | install | {Product.Name} {Product.Version} | " +
                $"dir=\"{target}\" | pages={string.Join(",", features)} | " +
                $"shortcuts={(shortcuts.Length == 0 ? "none" : string.Join(",", shortcuts))} | " +
                $"files={files} | size={sizeMb} MB";

            AppendLog(target, line);
        }
        catch {  }
    }

    private static void AppendLog(string target, string line)
    {
        var log = Path.Combine(target, "log.txt");
        var text = File.Exists(log) ? File.ReadAllText(log) + Environment.NewLine : "";
        File.WriteAllText(log, text + line + Environment.NewLine);
    }

    private static async Task ExtractPayloadAsync(string target, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        await using var payload = typeof(InstallService).Assembly
            .GetManifestResourceStream(Product.PayloadResource)
            ?? throw new InvalidOperationException(
                "The application files are not embedded in this installer. Build payload.zip first (see build-installer.ps1).");

        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        long total = 0;
        foreach (var entry in archive.Entries)
            total += entry.Length;
        if (total <= 0) total = 1;

        long done = 0;
        var buffer = new byte[81920];

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!destination.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Invalid path in archive: " + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using (var input = entry.Open())
            await using (var output = File.Create(destination))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    int pct = (int)Math.Min(90, done * 90 / total);
                    progress.Report(new ProgressInfo(pct, "Extracting: " + entry.FullName));
                }
            }
        }
    }

    private static void CreateShortcuts(string target, InstallOptions options)
    {
        var exe = Path.Combine(target, Product.AppExe);
        if (options.DesktopShortcut) CreateShortcut(Product.DesktopShortcut, exe, target);
        if (options.StartMenuShortcut) CreateShortcut(Product.StartMenuShortcut, exe, target);
    }

    private static void CreateShortcut(string linkPath, string exe, string workingDir)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = exe;
            link.WorkingDirectory = workingDir;
            link.IconLocation = exe + ",0";
            link.Description = Product.Name;
            link.Save();
        }
        catch { /* a shortcut is not critical */ }
    }

    private static void PinToTaskbar(string target)
    {
        var exe = Path.Combine(target, Product.AppExe);

        // Preferred: ask the shell for the "Pin to taskbar" verb.
        if (TryPinVerb(exe)) return;

        // Fallback: drop a shortcut into the pinned-items folder.
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            Directory.CreateDirectory(folder);
            CreateShortcut(Path.Combine(folder, Product.Name + ".lnk"), exe, target);
        }
        catch { /* pinning is best-effort */ }
    }

    private static bool TryPinVerb(string exe)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.Namespace(Path.GetDirectoryName(exe));
            dynamic item = folder.ParseName(Path.GetFileName(exe));
            dynamic verbs = item.Verbs();

            for (int i = 0; i < verbs.Count; i++)
            {
                dynamic verb = verbs.Item(i);
                string name = ((string)verb.Name).Replace("&", "").Trim();
                if (name.Equals("Pin to taskbar", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Unpin from taskbar", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static void CreateUninstaller(string target)
    {
        var destination = Path.Combine(target, Product.UninstallExe);
        try { if (File.Exists(destination)) File.Delete(destination); } catch { }

        // Preferred: the slim uninstaller embedded at build time - it carries no
        // app payload, so it weighs ~15 MB instead of a full copy of the setup.
        try
        {
            using var src = typeof(InstallService).Assembly
                .GetManifestResourceStream("uninstaller.exe");
            if (src != null)
            {
                using var dst = File.Create(destination);
                src.CopyTo(dst);
                return;
            }
        }
        catch { /* no embedded uninstaller - fall back to the legacy scheme */ }

        // Legacy fallback (old releases): a hard link to the full installer on
        // the same volume, so the large binary at least is not duplicated.
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current)) return;

        if (!NativeMethods.CreateHardLink(destination, current, IntPtr.Zero))
        {
            try { File.Copy(current, destination, true); } catch { /* fallback failed */ }
        }
    }

    private static void WriteUninstallRegistry(string target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Product.RegistryKey, true);
        if (key == null) return;

        var exe = Path.Combine(target, Product.AppExe);
        var uninstall = Path.Combine(target, Product.UninstallExe);

        key.SetValue("DisplayName", Product.Name);
        key.SetValue("DisplayVersion", Product.Version);
        key.SetValue("Publisher", Product.Publisher);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", target);
        key.SetValue("UninstallString", $"\"{uninstall}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstall}\" --uninstall --silent");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        try { key.SetValue("EstimatedSize", (int)(DirSize(target) / 1024)); } catch { }
    }

    public static void LaunchApp(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(target, Product.AppExe))
            {
                WorkingDirectory = target,
                UseShellExecute = true
            });
        }
        catch { }
    }

    // ──────────────────────────── Uninstall ────────────────────────────

    public static async Task UninstallAsync(string target, bool removeData, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        progress.Report(new ProgressInfo(5, "Closing the application..."));
        await Task.Run(() => KillApp(), ct);


        AppendLog(target, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | uninstall | {Product.Name} {Product.Version} | " +
                          $"remove-data={removeData.ToString().ToLowerInvariant()}");

        progress.Report(new ProgressInfo(25, "Removing shortcuts..."));
        TryDelete(Product.DesktopShortcut);
        TryDelete(Product.StartMenuShortcut);
        TryDelete(PinnedTaskbarShortcut());

        progress.Report(new ProgressInfo(45, "Cleaning up the registry..."));
        try { Registry.CurrentUser.DeleteSubKeyTree(Product.RegistryKey, false); } catch { }

        if (removeData)
        {
            progress.Report(new ProgressInfo(65, "Deleting user settings..."));
            TryDeleteDir(Product.UserDataDir);
        }

        progress.Report(new ProgressInfo(80, "Deleting files..."));
        await Task.Run(() =>
        {
            if (!Directory.Exists(target)) return;
            foreach (var file in Directory.GetFiles(target))
            {
                if (string.Equals(Path.GetFileName(file), Product.UninstallExe, StringComparison.OrdinalIgnoreCase))
                    continue;
                TryDelete(file);
            }
            foreach (var dir in Directory.GetDirectories(target))
                TryDeleteDir(dir);
        }, ct);

        progress.Report(new ProgressInfo(95, "Finishing..."));
        ScheduleSelfDelete(target);
        progress.Report(new ProgressInfo(100, "Uninstalled"));
    }

    private static string PinnedTaskbarShortcut()
    {
        try
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\" + Product.Name + ".lnk");
        }
        catch { return string.Empty; }
    }

    private static void KillApp()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Product.AppExe)))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }
        catch { }
    }

    private static void ScheduleSelfDelete(string target)
    {
        // Uninstall.exe is still running — let cmd delete it and the folder after we exit.
        try
        {
            var self = Path.Combine(target, Product.UninstallExe);
            var args = $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{self}\" & rmdir /s /q \"{target}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch { }
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static long DirSize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                try { size += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return size;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
