using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class SettingsPage : UserControl
{
    private readonly SettingsService _settings;
    private readonly string _currentTheme;

    private string _activeTheme;
    private readonly Action<string> _applyTheme;
    private readonly Action _resetAll;
    private readonly UpdateService _update;

    private readonly (string Name, string Label, string Preview, string Accent)[] _themes = new[]
    {
        ("black", "Black", "#111114", "#7c5cfc"),
        ("alucard", "Alucard", "#120205", "#c1121f"),
        ("glass-black", "Glass", "#0d0d11", "#60a5fa"),
        ("retro", "Retro", "#1a1a2e", "#fbbf24"),
        ("pink", "Pink", "#1e1520", "#f472b6"),
        ("slack", "Slack", "#1c1e20", "#94a3b8"),
        ("cyber", "Cyber", "#111620", "#34d399"),
        ("mono", "Mono", "#1a1a1a", "#a1a1aa"),
        ("nord", "Nord", "#3b4252", "#88c0d0"),
        ("dracula", "Dracula", "#282a36", "#bd93f9"),
        ("amoled", "Amoled", "#050506", "#ff3d84"),
    };

    public SettingsPage(SettingsService settings, string currentTheme, Action<string> applyTheme,
                        Action resetAll, UpdateService update)
    {
        _settings = settings;
        _currentTheme = currentTheme;
        _activeTheme = currentTheme;
        _applyTheme = applyTheme;
        _resetAll = resetAll;
        _update = update;
        InitializeComponent();
        Loaded += OnLoaded;



        DetachedFromVisualTree += (_, _) => _update.StateChanged -= OnUpdateStateChanged;
        AttachedToVisualTree += (_, _) => _update.StateChanged += OnUpdateStateChanged;
    }

    private void OnUpdateStateChanged() =>
        Dispatcher.UIThread.Post(RefreshUpdateUi, DispatcherPriority.Background);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SetupThemes();
        SetupStyles();
        SetupActionButtons();
        SetupUpdates();


        var ver = this.FindControl<TextBlock>("VersionText");
        if (ver != null) ver.Text = $"v{UpdateService.CurrentVersion}";
    }

    // ── Updates (#14) ───────────────────────────────────────────────────────

    private void SetupUpdates()
    {
        var combo = this.FindControl<ComboBox>("UpdateModeCombo");
        if (combo != null)
        {
            combo.Items?.Add("Ask before download");
            combo.Items?.Add("Automatic");
            combo.SelectedItem = _settings.GetSettings().UpdateMode == "auto" ? "Automatic" : "Ask before download";

            combo.SelectionChanged += (_, _) =>
            {
                var mode = combo.SelectedItem as string == "Automatic" ? "auto" : "ask";
                if (mode == _settings.GetSettings().UpdateMode) return;

                _settings.GetSettings().UpdateMode = mode;
                _settings.Save(_settings.GetSettings());


                if (mode == "auto") _update.StartCheck(autoDownload: true);
            };
        }

        var check = this.FindControl<Button>("UpdateCheckBtn");
        if (check != null) check.Click += (_, _) => _update.StartCheck(IsAutoMode());

        var action = this.FindControl<Button>("UpdateActionBtn");
        if (action != null) action.Click += (_, _) => OnUpdateAction();

        RefreshUpdateUi();
    }

    private bool IsAutoMode() =>
        string.Equals(_settings.GetSettings().UpdateMode, "auto", StringComparison.OrdinalIgnoreCase);

    private void OnUpdateAction()
    {
        switch (_update.Stage)
        {
            case UpdateStage.Available:

                _update.StartDownload();
                break;

            case UpdateStage.Downloaded:
                if (!_update.TryInstall()) return;


                if (Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                break;
        }
    }

    private void RefreshUpdateUi()
    {
        var state = this.FindControl<TextBlock>("UpdateStateText");
        var notes = this.FindControl<TextBlock>("UpdateNotesText");
        var action = this.FindControl<Button>("UpdateActionBtn");
        if (state == null || action == null) return;

        void Show(string text, string color, bool button, string buttonText)
        {
            state.Text = text;
            state.Foreground = new SolidColorBrush(Color.Parse(color));
            action.IsVisible = button;
            action.Content = buttonText;
        }

        switch (_update.Stage)
        {
            case UpdateStage.Checking:
                Show("Checking for updates...", "#9d9da5", false, "Download");
                break;

            case UpdateStage.UpToDate:
                Show($"You are up to date — v{UpdateService.CurrentVersion}", "#34d399", false, "Download");
                break;

            case UpdateStage.Available:
                Show($"Update available — v{_update.LatestVersion}", "#fbbf24", true, "Download");
                break;

            case UpdateStage.Downloading:
                Show($"Downloading... {_update.Progress}%", "#60a5fa", false, "Download");
                break;

            case UpdateStage.Downloaded when _update.IsInstalled:
                Show($"Update v{_update.LatestVersion} is ready to install", "#34d399", true, "Install & restart");
                break;


            case UpdateStage.Downloaded:
                Show($"Update available — v{_update.LatestVersion}", "#fbbf24", true, "Download");
                break;

            case UpdateStage.Failed:
                Show(string.IsNullOrEmpty(_update.Error) ? "Could not reach GitHub" : $"Update check failed: {_update.Error}",
                     "#f87171", true, "Retry");
                break;

            default:
                Show("Update status unknown", "#5c5c66", true, "Check now");
                break;
        }

        if (notes == null) return;
        notes.Text = _update.Stage switch
        {
            UpdateStage.Available or UpdateStage.Downloaded => FirstLines(_update.ReleaseNotes),
            UpdateStage.UpToDate => "New releases are announced on the GitHub repo.",
            _ => ""
        };
    }


    private static string FirstLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r", "").Split('\n');
        return string.Join(" ", lines.Where(l => l.Trim().Length > 0).Take(2)).Trim();
    }

    private void SetupThemes()
    {
        var wrap = this.FindControl<WrapPanel>("ThemeWrap");
        if (wrap == null) return;

        foreach (var (name, label, preview, accent) in _themes)
            wrap.Children.Add(BuildThemeCard(wrap, name, label, preview, accent, isCustom: false));


        wrap.Children.Add(BuildThemeCard(wrap, "custom", "Custom", "#2a2a32", "#a1a1aa", isCustom: true));
    }



    private StackPanel BuildThemeCard(WrapPanel wrap, string name, string label, string preview, string accent, bool isCustom)
    {
        var btn = new Button { Content = label, Tag = name, MinWidth = 104, Height = 46 };
        AutomationProperties.SetAutomationId(btn, "Theme_" + name);


        if (isCustom)
        {
            var labelStack = new StackPanel { Orientation = Orientation.Horizontal };
            labelStack.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            labelStack.Children.Add(new Border
            {
                Classes = { "new-badge" },
                Child = new TextBlock { Text = "NEW" }
            });
            btn.Content = labelStack;
        }
        btn.Classes.Add("theme-btn");
        btn.Margin = new Thickness(6, 5);
        btn.Background = new SolidColorBrush(Color.Parse(preview));
        btn.Foreground = new SolidColorBrush(Color.Parse("#ffffff"));

        var accentBar = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(accent)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(6, 0),
            Opacity = 0.0
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(btn);
        stack.Children.Add(accentBar);

        var wrapper = new StackPanel();
        wrapper.Children.Add(stack);

        btn.Click += (_, _) =>
        {
            if (isCustom)
            {
                OpenThemeEditor(wrap, accentBar);
                return;
            }

            _activeTheme = name;
            _applyTheme(name);
            RefreshThemeHighlight(wrap, accentBar);
        };

        return wrapper;
    }

    private void OpenThemeEditor(WrapPanel wrap, Border accentBar)
    {
        var editor = new ThemeEditorWindow(_settings, theme =>
        {
            _activeTheme = theme;
            _applyTheme(theme);
        });

        editor.Closed += (_, _) => RefreshThemeHighlight(wrap, accentBar);

        if (this.VisualRoot is Window owner)
        {

            _ = editor.ShowDialog(owner);
        }
        else
        {
            editor.Show();
        }
    }


    private void RefreshThemeHighlight(WrapPanel wrap, Border? clickedBar)
    {
        foreach (var c in wrap.Children)
        {
            if (c is not StackPanel w || w.Children.Count == 0) continue;
            if (w.Children[0] is not StackPanel s || s.Children.Count < 2) continue;

            if (s.Children[0] is not Button b || s.Children[1] is not Border bar) continue;

            var isActive = b.Tag?.ToString() == _activeTheme;
            var accent = bar is { Background: SolidColorBrush sb } ? sb.Color : Color.Parse("#a1a1aa");

            b.BorderBrush = new SolidColorBrush(isActive ? accent : Color.Parse("#35353d"));
            b.BorderThickness = new Thickness(isActive ? 2 : 1);
            bar.Opacity = isActive ? 1.0 : 0.0;
        }
    }


    // ── Style ───────────────────────────────────────────────────────────────

    private static readonly (string Mode, string Label)[] ButtonStyles =
    {
        ("default", "Default"), ("glass", "Glass"), ("flat", "Flat"),
    };

    private static readonly string[] FontStyleCandidates =
    {
        "Cascadia Code", "Cascadia Mono", "Consolas", "JetBrains Mono", "Roboto Mono",
        "Inter", "Segoe UI", "Roboto", "Arial", "Verdana", "Tahoma", "Georgia", "Courier New",
    };

    private void SetupStyles()
    {
        SetupFontStyle();
        SetupButtonStyles();
    }

    private void SetupFontStyle()
    {
        var combo = this.FindControl<ComboBox>("FontStyleCombo");
        var preview = this.FindControl<TextBlock>("FontStylePreview");
        if (combo == null) return;

        const string themeDefault = "Theme default";

        combo.Items?.Add(themeDefault);
        foreach (var f in FontStyleCandidates) combo.Items?.Add(f);

        var current = _settings.GetSettings().UiFont;
        combo.SelectedItem = string.IsNullOrWhiteSpace(current) || !FontStyleCandidates.Contains(current)
            ? themeDefault
            : current;
        if (preview != null && combo.SelectedItem is string sel && sel != themeDefault)
            preview.FontFamily = new FontFamily(sel);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string f) return;

            var s = _settings.GetSettings();
            s.UiFont = f == themeDefault ? "" : f;
            _settings.Save(s);

            if (preview != null)
                preview.FontFamily = f == themeDefault ? FontFamily.Default : new FontFamily(f);

            _applyTheme(_activeTheme);
        };
    }

    private void SetupButtonStyles()
    {
        var wrap = this.FindControl<WrapPanel>("ButtonStyleWrap");
        if (wrap == null) return;

        foreach (var (mode, label) in ButtonStyles)
        {
            var chip = new Button
            {
                Content = label,
                Tag = mode,
                Classes = { "ghost" },
                MinWidth = 96,
                Height = 34,
                Margin = new Thickness(6, 5),
                // The chip itself is drawn in its own style: glass = round, flat = sharp.
                CornerRadius = mode switch
                {
                    "glass" => new CornerRadius(15),
                    "flat" => new CornerRadius(2),
                    _ => new CornerRadius(9)
                },
            };
            AutomationProperties.SetAutomationId(chip, "BtnStyle" + label);
            wrap.Children.Add(chip);
        }

        foreach (var c in wrap.Children)
            if (c is Button chip)
                chip.Click += (_, _) => SelectButtonStyle(chip);

        RefreshButtonStyles();
    }

    private void SelectButtonStyle(Button chip)
    {
        var mode = chip.Tag?.ToString() ?? "default";

        var s = _settings.GetSettings();
        s.UiButtonMode = mode;
        _settings.Save(s);

        RefreshButtonStyles();
        _applyTheme(_activeTheme);
    }

    private void RefreshButtonStyles()
    {
        var wrap = this.FindControl<WrapPanel>("ButtonStyleWrap");
        if (wrap == null) return;

        var active = _settings.GetSettings().UiButtonMode;
        if (string.IsNullOrWhiteSpace(active)) active = "default";

        var accent = Color.Parse("#7c5cfc");
        if (Application.Current?.Resources.TryGetResource("AccentDefault", null, out var res) is true
            && res is SolidColorBrush accentBrush)
            accent = accentBrush.Color;

        foreach (var c in wrap.Children)
        {
            if (c is not Button b) continue;

            var isActive = b.Tag?.ToString() == active;
            b.BorderThickness = new Thickness(isActive ? 2 : 1);
            b.BorderBrush = new SolidColorBrush(isActive ? accent : Color.Parse("#35353d"));
            b.Foreground = new SolidColorBrush(isActive ? accent : Color.Parse("#9d9da5"));
        }
    }

    private static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI");

    private void SetupActionButtons()
    {
        var resetBtn = this.FindControl<Button>("ResetAllBtn");
        if (resetBtn != null) resetBtn.Click += (_, _) => _resetAll();

        var folderBtn = this.FindControl<Button>("OpenFolderBtn");
        if (folderBtn != null) folderBtn.Click += (_, _) => OpenDataFolder();

        var exportBtn = this.FindControl<Button>("ExportBtn");
        if (exportBtn != null) exportBtn.Click += (_, _) => ExportConfig();
    }



    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            Process.Start(new ProcessStartInfo { FileName = DataFolder, UseShellExecute = true });
        }
        catch
        {

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    UseShellExecute = true
                });
            }
            catch {  }
        }
    }

    private void ExportConfig()
    {
        try
        {
            var src = Path.Combine(DataFolder, "settings.json");
            if (!File.Exists(src)) return;

            var dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                   "void_settings_export.json");
            File.Copy(src, dst, true);

            var hint = this.FindControl<TextBlock>("ExportHint");
            if (hint != null) hint.Text = $"Exported to {dst}";
        }
        catch {  }
    }
}