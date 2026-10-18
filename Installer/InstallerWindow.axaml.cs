using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Win32;

namespace VoidUI.Installer;

public partial class InstallerWindow : Window
{
    private enum Step { Welcome, License, Path, Features, Progress, Finish }

    private const string LicenseBody =
        "CUSTOM SOURCE-AVAILABLE LICENSE\n\n" +
        "Copyright (c) 2026 zeuthe (Sweety Void / https://github.com/zeuthe/sweety-void). All Rights Reserved.\n\n" +
        "1. FORKING & MODIFICATION\n" +
        "You are permitted to create forks, view, analyze, modify and improve the source code of this " +
        "Project for personal, educational, non-commercial or commercial purposes, subject to the " +
        "conditions set out below.\n\n" +
        "2. ATTRIBUTION\n" +
        "When publishing, distributing or demonstrating any modified version (fork) or product created " +
        "on the basis of this Project, you MUST preserve an explicit indication of the original Project " +
        "and its author, and include a link to the official repository/source of the original Project in " +
        "the description of your product and in its interface (the \"About\" section).\n\n" +
        "3. DONATION & SALES INTEGRITY\n" +
        "In any fork, build, modified version or derived product it is FORBIDDEN to remove, hide, " +
        "modify, substitute or block the built-in links or modules for collecting donations, the sales " +
        "systems or the financial support channels of the original author, or to redirect the " +
        "built-in payment mechanisms of the original author to your own payment details.\n\n" +
        "4. NO MALWARE / MALICIOUS CODE\n" +
        "It is prohibited to use the code of this Project to create, distribute or integrate malicious " +
        "software (malware, spyware, trojans, ransomware, adware, etc.), hidden miners, personal data " +
        "scrapers, keyloggers or any components that compromise the security or privacy of users.\n\n" +
        "5. NO ADS & NO PAID SUBSCRIPTIONS\n" +
        "Modified versions and builds must not embed third-party advertising banners, trackers, " +
        "pop-ups or advertising networks, and must not introduce paid subscriptions or paywalls over " +
        "the basic functionality of the Project, with the exception of the official sales features " +
        "laid down by the original author.\n\n" +
        "6. DISCLAIMER OF WARRANTY\n" +
        "The software is provided \"AS IS\", without warranty of any kind. The author shall not be " +
        "liable for any losses or problems arising from the use of this software or of its forks. " +
        "The full text (English + original Russian) is in LICENSE.txt in the repository.\n\n" +
        "THIRD-PARTY COMPONENTS\n\n" +
        "This application bundles third-party open-source components, including Avalonia UI (MIT), the .NET " +
        "runtime and Windows Desktop runtime (MIT), Newtonsoft.Json (MIT) and QRCoder (MIT). Their licenses apply " +
        "to those components only.\n\n" +
        "DISCLAIMER\n\n" +
        "Sweety Void is an unofficial tool and is not affiliated with or endorsed by any game developer. Use it at " +
        "your own risk. The author is not responsible for account bans, lost progress or any other damage.";

    private readonly bool _uninstallMode;
    private Step _step;
    private string _target = Product.DefaultInstallDir;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _uninstallRunning;

    public InstallerWindow(string[] args)
    {
        InitializeComponent();
        _uninstallMode = args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

        this.FindControl<TextBlock>("LicenseText")!.Text = LicenseBody;
        this.FindControl<TextBox>("PathBox")!.Text = _target;

        Wire();

        if (_uninstallMode)
        {
            this.FindControl<TextBlock>("TitleText")!.Text = "Sweety Void Uninstaller";
            Title = "Sweety Void Uninstaller";
            _ = UninstallUiAsync();
        }
        else
        {
            ShowStep(Step.Welcome);
        }
    }

    private void Wire()
    {
        this.FindControl<Grid>("TitleBar")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();

        this.FindControl<Button>("NextBtn")!.Click += async (_, _) => await OnNextAsync();
        this.FindControl<Button>("BackBtn")!.Click += (_, _) => OnBack();
        this.FindControl<Button>("CancelBtn")!.Click += (_, _) => Close();
        this.FindControl<Button>("BrowseBtn")!.Click += async (_, _) => await BrowseAsync();

        foreach (var name in new[] { "AcceptLicenseCheck", "AcceptThirdPartyCheck", "AcceptRiskCheck" })
            this.FindControl<CheckBox>(name)!.IsCheckedChanged += (_, _) => UpdateButtons();
    }

    // ────────────────────────────── Navigation ──────────────────────────────

    private void ShowStep(Step step)
    {
        _step = step;
        SetVisible("PageWelcome", step == Step.Welcome);
        SetVisible("PageLicense", step == Step.License);
        SetVisible("PagePath", step == Step.Path);
        SetVisible("PageFeatures", step == Step.Features);
        SetVisible("PageProgress", step == Step.Progress);
        SetVisible("PageFinish", step == Step.Finish);
        UpdateButtons();
    }

    private bool LicenseAccepted =>
        this.FindControl<CheckBox>("AcceptLicenseCheck")!.IsChecked == true &&
        this.FindControl<CheckBox>("AcceptThirdPartyCheck")!.IsChecked == true &&
        this.FindControl<CheckBox>("AcceptRiskCheck")!.IsChecked == true;

    private void UpdateButtons()
    {
        var back = this.FindControl<Button>("BackBtn")!;
        var next = this.FindControl<Button>("NextBtn")!;
        var cancel = this.FindControl<Button>("CancelBtn")!;

        // The uninstaller is a single progress screen - no navigation at all.
        back.IsVisible = !_busy && !_uninstallRunning && _step is Step.License or Step.Path or Step.Features;
        cancel.IsVisible = !_busy && !_uninstallRunning && _step != Step.Finish;

        next.IsVisible = !_busy && !_uninstallRunning;
        next.IsEnabled = _step != Step.License || LicenseAccepted;
        next.Content = _step switch
        {
            Step.Welcome => "Next",
            Step.License => "Next",
            Step.Path => "Next",
            Step.Features => "Install",
            Step.Finish => "Finish",
            _ => "Next"
        };

        if (_uninstallRunning) return;

        if (_step == Step.License && !LicenseAccepted)
            SetStatus("Accept all three agreements to continue");
        else if (_step == Step.Welcome)
            SetStatus("Sweety Void Setup 1.0.0");
    }

    private async Task OnNextAsync()
    {
        switch (_step)
        {
            case Step.Welcome:
                ShowStep(Step.License);
                break;
            case Step.License:
                ShowStep(Step.Path);
                break;
            case Step.Path:
                ShowStep(Step.Features);
                break;
            case Step.Features:
                await RunInstallAsync();
                break;
            case Step.Finish:
                FinishAndClose();
                break;
        }
    }

    private void OnBack()
    {
        if (_step == Step.License) ShowStep(Step.Welcome);
        else if (_step == Step.Path) ShowStep(Step.License);
        else if (_step == Step.Features) ShowStep(Step.Path);
    }

    private async Task BrowseAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the install folder",
            AllowMultiple = false
        });

        var picked = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(picked))
        {
            _target = Path.Combine(picked, "Sweety Void");
            this.FindControl<TextBox>("PathBox")!.Text = _target;
        }
    }

    // ────────────────────────────── Install ──────────────────────────────

    private async Task RunInstallAsync()
    {
        var pathBox = this.FindControl<TextBox>("PathBox")!.Text;
        if (string.IsNullOrWhiteSpace(pathBox))
        {
            SetStatus("Enter an install folder");
            return;
        }

        try { _target = Path.GetFullPath(pathBox.Trim()); }
        catch { SetStatus("Invalid path"); return; }

        var options = new InstallOptions(
            _target,
            this.FindControl<CheckBox>("DesktopCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("StartMenuCheck")!.IsChecked == true,
            this.FindControl<CheckBox>("PinTaskbarCheck")!.IsChecked == true,

            this.FindControl<CheckBox>("TrackerCheck")!.IsChecked != false,
            this.FindControl<CheckBox>("GraphicsCheck")!.IsChecked != false,
            this.FindControl<CheckBox>("BindsCheck")!.IsChecked != false);

        _busy = true;
        Set("ProgressTitle", "Installing...");
        ShowStep(Step.Progress);
        SetStatus("Installing to " + _target);

        var progress = new Progress<ProgressInfo>(p =>
        {
            this.FindControl<ProgressBar>("Progress")!.Value = p.Percent;
            this.FindControl<TextBlock>("ProgressPercent")!.Text = p.Percent + "%";
            this.FindControl<TextBlock>("ProgressDetail")!.Text = p.Detail;
        });

        _cts = new CancellationTokenSource();
        try
        {
            await InstallService.InstallAsync(options, progress, _cts.Token);
            _busy = false;
            Set("FinishTitle", "Installation complete");
            Set("FinishText", "Sweety Void has been installed to:\n" + _target);
            this.FindControl<CheckBox>("LaunchCheck")!.IsVisible = true;
            ShowStep(Step.Finish);
        }
        catch (OperationCanceledException)
        {
            _busy = false;
            ShowStep(Step.Path);
        }
        catch (Exception ex)
        {
            _busy = false;
            SetStatus("Error: " + ex.Message);
            ShowStep(Step.Path);
        }
    }

    /// The app starts only when Finish is pressed and the launch box is ticked.
    private void FinishAndClose()
    {
        if (this.FindControl<CheckBox>("LaunchCheck")!.IsChecked == true &&
            this.FindControl<CheckBox>("LaunchCheck")!.IsVisible)
            InstallService.LaunchApp(_target);

        Close();
    }

    // ───────────────────────────── Uninstall ─────────────────────────────

    /// One screen: a progress bar and a farewell. No questions, closes when done.
    private async Task UninstallUiAsync()
    {
        var target = ReadInstalledDir() ?? Product.DefaultInstallDir;
        _target = target;

        _busy = true;
        _uninstallRunning = true;
        Set("ProgressTitle", "Sorry to see you go :(");
        SetStatus("Uninstalling Sweety Void from " + target);
        ShowStep(Step.Progress);
        UpdateButtons();

        // Let the message be read - the removal itself takes a fraction of a second.
        await Task.Delay(1400);

        var progress = new Progress<ProgressInfo>(p =>
        {
            this.FindControl<ProgressBar>("Progress")!.Value = p.Percent;
            this.FindControl<TextBlock>("ProgressPercent")!.Text = p.Percent + "%";
            this.FindControl<TextBlock>("ProgressDetail")!.Text = p.Detail;
        });

        _cts = new CancellationTokenSource();
        try
        {
            await InstallService.UninstallAsync(_target, false, progress, _cts.Token);
        }
        catch { }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        Set("ProgressTitle", "Uninstalled. Goodbye!");
        await Task.Delay(900);
        Close();
    }

    private static string? ReadInstalledDir()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Product.RegistryKey);
            return key?.GetValue("InstallLocation") as string;
        }
        catch { return null; }
    }

    // ────────────────────────────── Helpers ──────────────────────────────

    private void SetVisible(string name, bool visible) =>
        this.FindControl<Control>(name)!.IsVisible = visible;

    private void Set(string name, string text) =>
        this.FindControl<TextBlock>(name)!.Text = text;

    private void SetStatus(string text) =>
        this.FindControl<TextBlock>("FooterStatus")!.Text = text;
}
