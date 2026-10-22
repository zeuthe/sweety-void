using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VoidUI.Services;

namespace VoidUI;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly SteamService _steamService;
    private readonly TrackerService _trackerService;



    private readonly UpdateService _updateService;


    private readonly FeatureFlags _features = FeatureFlags.Load();

    private string _currentTheme = "black";

    private OverlayWindow? _overlayWindow;
    private System.Windows.Threading.Dispatcher? _overlayDispatcher;
    private System.Threading.Thread? _overlayThread;
    private System.Threading.ManualResetEventSlim? _overlayReady;
    private System.Timers.Timer? _themeWatcher;
    private Avalonia.Threading.DispatcherTimer? _statusTimer;
    private int _statusTick;


    private string _doneCaption = "";
    private int _doneTicks;
    private bool _doneExpired;
    private string _workingFile = "output_log.txt";
    private bool _rustPathExpanded;
    private string? _rustFullPath;
    private bool _targetOk;
    private bool _rustMissing;


    private string? _cfgBusyFile;


    private bool _workStarted;

    public MainWindow()
    {
        _settings = new SettingsService();
        _steamService = new SteamService();
        _trackerService = new TrackerService(_steamService);
        _updateService = new UpdateService();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Presets moved out of the install dir so updates can never clobber
        // them; sync anything an older install (or a new release) left behind.
        Services.UserPresets.MigrateFromInstallDir();

        SetupDrag();
        WireTabButtons();
        WireControlButtons();
        WireSearch();
        StartStatusBar();
        ApplyTheme(_settings.GetSettings().Theme ?? "black");
        SizeChanged += (_, _) => { if (_currentTheme == "alucard") BuildAlucardDecor("alucard"); };
        NavigateTo("home");
        StartOverlay();
        StartThemeWatcher();
        StartUpdateCheck();
        WireStatusHint();
    }



    private void StartUpdateCheck()
    {
        try
        {
            var auto = string.Equals(_settings.GetSettings().UpdateMode, "auto",
                StringComparison.OrdinalIgnoreCase);


            _ = Task.Run(() => _updateService.StartCheck(auto));
        }
        catch {  }
    }

    #region Drag

    private void SetupDrag()
    {
        var topBar = this.FindControl<Border>("TopBar");
        if (topBar == null) return;
        topBar.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    #endregion

    #region Tab Navigation

    private void WireTabButtons()
    {
        var tabs = new[] { ("TabGraphics", "graphics"), ("TabBinds", "binds"), ("TabHelpers", "helpers"),
                           ("TabCrosshair", "crosshair"), ("TabTracker", "tracker") };
        foreach (var (name, page) in tabs)
        {
            var btn = this.FindControl<Button>(name);
            if (btn == null) continue;



            if (!_features.IsEnabled(page))
            {
                btn.IsVisible = false;
                continue;
            }

            btn.Click += (_, _) => NavigateTo(page);
        }

        var logoBtn = this.FindControl<Button>("LogoBtn");
        if (logoBtn != null) logoBtn.Click += (_, _) => NavigateTo("home");
    }

    private void UpdateTabActive(string page)
    {
        var tabNames = new Dictionary<string, string>
        {
            ["graphics"] = "TabGraphics", ["binds"] = "TabBinds", ["helpers"] = "TabHelpers",
            ["crosshair"] = "TabCrosshair", ["tracker"] = "TabTracker"
        };
        foreach (var (pg, name) in tabNames)
        {
            var btn = this.FindControl<Button>(name);
            if (btn == null) continue;
            if (pg == page) btn.Classes.Add("active"); else btn.Classes.Remove("active");
        }
    }

    #endregion

    #region Global Search

    private sealed record SearchEntry(string Label, string Category, string Kind, string Value);
    private bool _searchGuard;

    private void WireSearch()
    {
        var box = this.FindControl<TextBox>("GlobalSearch");
        var popup = this.FindControl<Popup>("SearchPopup");
        var list = this.FindControl<ListBox>("SearchList");
        if (box == null || popup == null || list == null) return;

        popup.PlacementTarget = box;

        box.TextChanged += (_, _) => UpdateSearchResults(box, popup, list);
        box.GotFocus += (_, _) => UpdateSearchResults(box, popup, list);
        box.KeyDown += (_, e) =>
        {
            if (!popup.IsOpen || list.ItemCount == 0) return;
            if (e.Key == Key.Down)
            {
                _searchGuard = true;
                list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.ItemCount - 1);
                _searchGuard = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _searchGuard = true;
                list.SelectedIndex = Math.Max(list.SelectedIndex - 1, -1);
                _searchGuard = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && list.SelectedItem is SearchEntry entry)
            {
                ExecuteSearchEntry(entry);
                CloseSearchBox(box, popup, list);
                e.Handled = true;
            }
        };

        list.SelectionChanged += (_, _) =>
        {
            if (_searchGuard) return;
            if (list.SelectedItem is not SearchEntry entry) return;
            ExecuteSearchEntry(entry);
            CloseSearchBox(box, popup, list);
        };
    }

    private void CloseSearchBox(TextBox box, Popup popup, ListBox list)
    {
        _searchGuard = true;
        list.SelectedItem = null;
        list.ItemsSource = null;
        _searchGuard = false;
        popup.IsOpen = false;
        box.Text = "";
    }

    private void UpdateSearchResults(TextBox box, Popup popup, ListBox list)
    {
        var q = box.Text?.Trim() ?? "";
        if (q.Length == 0)
        {
            popup.IsOpen = false;
            list.ItemsSource = null;
            return;
        }

        var ql = q.ToLowerInvariant();
        var hits = BuildSearchIndex()
            .Where(s => s.Label.ToLowerInvariant().Contains(ql) || s.Category.ToLowerInvariant().Contains(ql))
            .Take(14)
            .ToList();

        _searchGuard = true;
        list.ItemsSource = hits;
        list.SelectedIndex = hits.Count > 0 ? 0 : -1;
        _searchGuard = false;
        popup.IsOpen = hits.Count > 0;
    }

    private List<SearchEntry> BuildSearchIndex()
    {
        var list = new List<SearchEntry>
        {
            new("Home", "Page", "page", "home"),
            new("Graphics", "Page", "page", "graphics"),
            new("Binds", "Page", "page", "binds"),
            new("Helpers", "Page", "page", "helpers"),
            new("Crosshair", "Page", "page", "crosshair"),
            new("Tracker", "Page", "page", "tracker"),
            new("Settings", "Page", "page", "settings"),

            new("Dot preset", "Crosshair preset", "preset", "dot"),
            new("Cross preset", "Crosshair preset", "preset", "cross"),
            new("Circle preset", "Crosshair preset", "preset", "circle"),
            new("Bracket preset", "Crosshair preset", "preset", "bracket"),
            
            new("Custom preset", "Crosshair preset", "preset", "custom"),

            new("Black theme", "Theme", "theme", "black"),
            new("Alucard theme", "Theme", "theme", "alucard"),
            new("Glass theme", "Theme", "theme", "glass-black"),
            new("Retro theme", "Theme", "theme", "retro"),
            new("Pink theme", "Theme", "theme", "pink"),
            new("Slack theme", "Theme", "theme", "slack"),
            new("Cyber theme", "Theme", "theme", "cyber"),
            new("Mono theme", "Theme", "theme", "mono"),
            new("Nord theme", "Theme", "theme", "nord"),
            new("Dracula theme", "Theme", "theme", "dracula"),
            new("Amoled theme", "Theme", "theme", "amoled")
        };

        try
        {
            var dir = Services.UserPresets.ConfigsDir;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    list.Add(new SearchEntry(Path.GetFileNameWithoutExtension(file), "Crosshair config", "config", file));
            }
        }
        catch { }
        return list;
    }

    private void ExecuteSearchEntry(SearchEntry entry)
    {
        void GoCrosshair()
        {
            NavigateTo("crosshair");
            var content = this.FindControl<ContentControl>("PageContent");
            if (content?.Content is Views.CrosshairPage page)
            {
                if (entry.Kind == "preset") page.SelectPreset(entry.Value);
                else page.LoadConfigFile(entry.Value);
            }
        }

        if (entry.Kind == "page")
        {
            NavigateTo(entry.Value);
        }
        else if (entry.Kind is "preset" or "config")
        {
            GoCrosshair();
            SetStatus(entry.Kind == "preset" ? $"Preset: {entry.Value}" : $"Loaded config: {entry.Label}");
        }
        else if (entry.Kind == "theme")
        {
            ApplyTheme(entry.Value);
            NavigateTo("settings");
        }
    }

    #endregion

    #region Control Buttons

    private void WireControlButtons()
    {
        var closeBtn = this.FindControl<Button>("CloseBtn");
        var minBtn = this.FindControl<Button>("MinBtn");
        var playBtn = this.FindControl<Button>("PlayBtn");
        var overlayBtn = this.FindControl<Button>("OverlayBtn");

        if (closeBtn != null) closeBtn.Click += (_, _) => Close();
        if (minBtn != null) minBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        if (playBtn != null) playBtn.Click += (_, _) => { _steamService.LaunchGame(); SetStatus("Launching game..."); };
        if (overlayBtn != null) overlayBtn.Click += (_, _) =>
        {
            _overlayDispatcher?.Invoke(() =>
            {
                if (_overlayWindow == null) return;
                if (_overlayWindow.IsVisible) _overlayWindow.Hide(); else _overlayWindow.Show();
            });
            SetStatus("Overlay toggled");
        };
    }

    #endregion

    #region Navigation

    private void NavigateTo(string pageName)
    {

        if (!_features.IsEnabled(pageName)) pageName = "home";

        UpdateTabActive(pageName);
        var content = this.FindControl<ContentControl>("PageContent");
        if (content == null) return;

        if (pageName is "crosshair" or "settings")
            _overlayReady?.Wait(2000);

        UserControl page = pageName switch
        {
            "home" => new Views.HomePage(NavigateTo),
            "graphics" => new Views.GraphicsPage(),
            "binds" => new Views.BindsPage(),
            "helpers" => new Views.HelpersPage(),
            "crosshair" => new Views.CrosshairPage(_settings, _overlayWindow, _overlayDispatcher),
            "tracker" => new Views.TrackerPage(_trackerService, _settings),
            "settings" => new Views.SettingsPage(_settings, _currentTheme, ApplyTheme, ResetAll, _updateService),
            _ => new Views.GraphicsPage()
        };
        content.Content = page;
        SetStatus($"Page: {pageName}");


        SetWorkingFile(pageName switch
        {
            "binds" => "cfg/keys.txt",
            "graphics" => "cfg/client.txt",
            _ => "output_log.txt"
        });
    }

    #endregion

    #region Theme


    private sealed record Palette(
        string Base, string Low, string Mid, string High, string Overlay,
        string BSubtle, string BDef, string BStrong,
        string TextPrimary, string TextSecondary, string TextTertiary, string TextDisabled,
        string Accent, string AccentHover, string AccentSubtle,
        string Font, string ButtonMode)
    {

        public static readonly Palette DefaultText = new(
            "", "", "", "", "", "", "", "",
            "#ededef", "#9d9da5", "#5c5c66", "#3a3a42",
            "", "", "", "", "");
    }

    private static Palette PaletteFor(string theme, Models.ThemeSettings custom)
    {

        Palette With(string b, string l, string m, string h, string o,
                     string bs, string bd, string bst,
                     string a, string ah, string asub, string mode) =>
            Palette.DefaultText with
            {
                Base = b, Low = l, Mid = m, High = h, Overlay = o,
                BSubtle = bs, BDef = bd, BStrong = bst,
                Accent = a, AccentHover = ah, AccentSubtle = asub, ButtonMode = mode
            };

        switch (theme)
        {
            case "glass-black":
                return With("#07070a", "#0d0d11", "#131318", "#19191f", "#202028",
                            "#16161b", "#1e1e25", "#2d2d36", "#60a5fa", "#7cb8ff", "#16233a", "glass");
            case "retro":
                return With("#0f0f1a", "#151525", "#1a1a2e", "#222240", "#2a2a50",
                            "#1a1a35", "#25254a", "#35356a", "#fbbf24", "#fcd34d", "#3a2f10", "default");
            case "pink":
                return With("#100a0e", "#17101a", "#1e1520", "#251a28", "#2d1f30",
                            "#201525", "#2d1a2a", "#3d2a3a", "#f472b6", "#f68fc7", "#3a1a2c", "default");
            case "slack":
                return With("#111315", "#161819", "#1c1e20", "#222426", "#282a2c",
                            "#1e2022", "#252729", "#323436", "#94a3b8", "#a8b8c8", "#252e38", "flat");
            case "cyber":
                return With("#070a0e", "#0c1018", "#111620", "#171c28", "#1d2330",
                            "#121825", "#1a2332", "#253040", "#34d399", "#5ee0b4", "#0f2a24", "default");
            case "mono":
                return With("#0c0c0c", "#111111", "#1a1a1a", "#222222", "#2a2a2a",
                            "#1a1a1a", "#2a2a2a", "#3a3a3a", "#a1a1aa", "#b4b4bc", "#262626", "flat");
            case "nord":
                return With("#232833", "#2e3440", "#3b4252", "#434c5e", "#4c566a",
                            "#2e3440", "#3b4252", "#4c566a", "#88c0d0", "#8fbcbb", "#263440", "default");
            case "dracula":
                return With("#1c1d26", "#21222c", "#282a36", "#2f3141", "#44475a",
                            "#25262f", "#2f3141", "#44475a", "#bd93f9", "#cea6f5", "#2c2440", "default");
            case "amoled":
                return With("#000000", "#050506", "#0b0b0d", "#121215", "#1a1a1e",
                            "#1b1b20", "#26262c", "#3a3a42", "#ff3d84", "#ff6ba3", "#3a1224", "flat");
            case "alucard":
                return With("#0a0002", "#120205", "#1a0308", "#24050c", "#2e0710",
                            "#2a0509", "#3a060d", "#5c0a17", "#c1121f", "#e01e2d", "#2b0509", "flat");
            case "custom" when custom != null:
                return new(
                    custom.SurfaceBase, custom.SurfaceLow, custom.SurfaceMid, custom.SurfaceHigh, custom.SurfaceOverlay,
                    custom.BorderSubtle, custom.BorderDefault, custom.BorderStrong,
                    custom.TextPrimary, custom.TextSecondary, custom.TextTertiary, custom.TextDisabled,
                    custom.AccentDefault, custom.AccentHover, custom.AccentSubtle,
                    custom.FontFamily, custom.ButtonMode);
            default:
                return With("#0c0c0e", "#111114", "#18181c", "#1f1f24", "#27272e",
                            "#1a1a1f", "#232329", "#35353d", "#7c5cfc", "#9178ff", "#2a2248", "default");
        }
    }

    private void ApplyTheme(string theme)
    {
        _currentTheme = theme;
        _settings.SetTheme(theme);

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        var p = PaletteFor(theme, _settings.GetSettings().CustomTheme);

        SetBrush("SurfaceBase", p.Base);
        SetBrush("SurfaceLow", p.Low);
        SetBrush("SurfaceMid", p.Mid);
        SetBrush("SurfaceHigh", p.High);
        SetBrush("SurfaceOverlay", p.Overlay);

        SetBrush("BorderSubtle", p.BSubtle);
        SetBrush("BorderDefault", p.BDef);
        SetBrush("BorderStrong", p.BStrong);



        SetBrush("TextPrimary", p.TextPrimary);
        SetBrush("TextSecondary", p.TextSecondary);
        SetBrush("TextTertiary", p.TextTertiary);
        SetBrush("TextDisabled", p.TextDisabled);

        SetBrush("AccentDefault", p.Accent);
        SetBrush("AccentHover", p.AccentHover);
        SetBrush("AccentSubtle", p.AccentSubtle);

        var style = _settings.GetSettings();
        ApplyFont(string.IsNullOrWhiteSpace(style.UiFont) ? p.Font : style.UiFont);
        ApplyButtonMode(string.IsNullOrWhiteSpace(style.UiButtonMode) ? p.ButtonMode : style.UiButtonMode);

        BuildAlucardDecor(theme);

        SetStatus($"Theme: {theme}");

        void SetBrush(string key, string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            if (resources.TryGetResource(key, null, out var existing) && existing is SolidColorBrush old)
            {
                old.Color = Color.Parse(hex);
            }
        }
    }


    private void ApplyFont(string font)
    {
        var resources = Application.Current?.Resources;
        if (resources == null || string.IsNullOrWhiteSpace(font)) return;

        try { resources["AppFontFamily"] = new FontFamily(font); }
        catch {  }
    }


    private void ApplyButtonMode(string mode)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;


        resources["BtnCorner"] = mode switch
        {
            "glass" => new CornerRadius(15),
            "flat" => new CornerRadius(2),
            _ => new CornerRadius(9)
        };



        if (resources.TryGetResource("BtnGhostBg", null, out var bg) && bg is SolidColorBrush bgBrush)
            bgBrush.Color = Color.Parse(mode switch
            {
                "glass" => "#1a1a1e",
                "flat" => "#00000000",
                _ => "#17171a"
            });

        if (resources.TryGetResource("BtnGhostBgHover", null, out var hov) && hov is SolidColorBrush hovBrush)
            hovBrush.Color = Color.Parse(mode switch
            {
                "glass" => "#24242a",
                "flat" => "#26262b",
                _ => "#26262b"
            });
    }

    private sealed record BloodTrickle(Border Segment, double Length, double SegmentH, double Offset, double Speed);

    private readonly List<BloodTrickle> _bloodTrickles = new();
    private Avalonia.Threading.DispatcherTimer? _bloodTimer;
    private double _bloodClock;

    // ── Alucard theme: blood trickles + gothic cross ───────────────────────

    private void BuildAlucardDecor(string theme)
    {
        var decor = this.FindControl<Canvas>("AlucardDecor");
        if (decor == null) return;

        decor.Children.Clear();
        _bloodTimer?.Stop();
        _bloodTimer = null;
        _bloodTrickles.Clear();

        decor.IsVisible = theme == "alucard";
        if (!decor.IsVisible) return;

        var w = Bounds.Width > 0 ? Bounds.Width : Width;
        var rnd = new Random(13);
        var cx = w * 0.63 - 7;

        // Blood: thin trickles hanging from the top edge, each ending in a drop.
        for (int i = 0; i < 30; i++)
        {
            double x = rnd.NextDouble() * w;
            double dw = 3 + rnd.NextDouble() * 6;
            double dh = 12 + rnd.NextDouble() * 34;
            if (x > cx - 22 && x < cx + 32) continue;   // keep the cross clear
            var red = rnd.NextDouble() < 0.35 ? "#c1121f" : "#7a0810";

            var body = new Border
            {
                Width = dw,
                Height = dh,
                CornerRadius = new CornerRadius(0, 0, dw / 2, dw / 2),
                Background = new SolidColorBrush(Color.Parse(red)) { Opacity = 0.8 },
            };
            Canvas.SetLeft(body, x);
            Canvas.SetTop(body, 0);
            decor.Children.Add(body);

            var segH = 3 + rnd.NextDouble() * 4;
            var seg = new Border
            {
                Width = dw,
                Height = segH,
                CornerRadius = new CornerRadius(0, 0, dw / 2, dw / 2),
                Background = new SolidColorBrush(Color.Parse("#e01e2d")) { Opacity = 0.95 },
            };
            Canvas.SetLeft(seg, x);
            Canvas.SetTop(seg, -segH);
            decor.Children.Add(seg);

            _bloodTrickles.Add(new BloodTrickle(seg, dh, segH, rnd.NextDouble(), 0.55 + rnd.NextDouble() * 0.8));
        }

        // Church: a gothic cross in the empty part of the title bar, bleeding.
        var crossV = new Border
        {
            Width = 6, Height = 32,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.Parse("#8b0a12")),
            BorderBrush = new SolidColorBrush(Color.Parse("#e01e2d")),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(crossV, cx);
        Canvas.SetTop(crossV, 4);
        decor.Children.Add(crossV);

        var crossH = new Border
        {
            Width = 24, Height = 6,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.Parse("#8b0a12")),
            BorderBrush = new SolidColorBrush(Color.Parse("#e01e2d")),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(crossH, cx - 9);
        Canvas.SetTop(crossH, 12);
        decor.Children.Add(crossH);

        StartBloodFlow();
    }

    // One bright pulse slides down every trickle, each at its own speed and
    // phase, so the blood looks like it is actually running.
    private void StartBloodFlow()
    {
        _bloodTimer?.Stop();
        _bloodClock = 0;

        _bloodTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _bloodTimer.Tick += (_, _) =>
        {
            _bloodClock += 0.06;
            foreach (var t in _bloodTrickles)
            {
                var p = (_bloodClock * t.Speed + t.Offset) % 1.0;
                Canvas.SetTop(t.Segment, -t.SegmentH + p * (t.Length + t.SegmentH));
                t.Segment.Opacity = p > 0.82 ? 0.95 * (1 - (p - 0.82) / 0.18) : 0.95;
            }
        };
        _bloodTimer.Start();
    }

    private void StartThemeWatcher()
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI", "settings.json");
        if (!File.Exists(settingsPath)) return;
        _themeWatcher = new System.Timers.Timer(2000);
        var lastWrite = File.GetLastWriteTimeUtc(settingsPath);
        _themeWatcher.Elapsed += (_, _) =>
        {
            try
            {
                var current = File.GetLastWriteTimeUtc(settingsPath);
                if (current != lastWrite)
                {
                    lastWrite = current;
                    var s = _settings.Load();
                    if (s.Theme != _currentTheme)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme(s.Theme ?? "black"));
                }
            }
            catch { }
        };
        _themeWatcher.AutoReset = true;
        _themeWatcher.Start();
    }

    #endregion

    #region Steam / Overlay


    private void StartStatusBar()
    {
        RefreshRustStatus();

        _statusTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTick++;




            var busy = IsBusy;
            if (busy != _workStarted)
            {
                _workStarted = busy;
                RefreshRustStatus();
            }
            else if (_statusTick % 25 == 0) RefreshRustStatus();

            var dots = this.FindControl<TextBlock>("StatusDots");
            if (dots == null || _rustMissing) return;
            if (_workStarted && _targetOk)
            {

                dots.Text = (_statusTick % 8) switch
                {
                    1 => ".  ",
                    2 => ".. ",
                    3 => "...",
                    4 => "...",
                    5 => ".. ",
                    6 => ".  ",
                    _ => "   "
                };
                return;
            }

            AnimateUpdateNotice(dots);
            UpdateStatusHint();
        };
        _statusTimer.Start();
    }



    private string BuildStatusHint()
    {
        if (_rustMissing)
            return "Rust was not found. Install the game or check the Steam library, then restart the app.";

        var tip = _updateService.Stage switch
        {
            UpdateStage.Downloading =>
                $"Downloading update v{_updateService.LatestVersion} — {_updateService.Progress}%. " +
                "The installer is saved to %LocalAppData%\\VoidUI\\updates and is installed only after you confirm.",
            UpdateStage.Downloaded when !_doneExpired =>
                "Update is downloaded and waiting. Install it from Settings → Updates; the app will restart.",
            UpdateStage.Available =>
                "Idle. A newer version is on GitHub — open Settings → Updates to download it.",
            _ => ""
        };

        if (!string.IsNullOrEmpty(tip)) return tip;

        return _workStarted
            ? _targetOk
                ? $"Processing {ActiveWorkFile} from the Rust folder right now."
                : $"Waiting for {ActiveWorkFile} — the game has not created it yet."
            : "Idle. Nothing is being processed; the tracker reads the log only while parsing runs.";
    }


    private void UpdateStatusHint()
    {
        var hint = this.FindControl<Border>("StatusHint");
        if (hint is not { IsVisible: true }) return;

        var text = this.FindControl<TextBlock>("StatusHintText");
        if (text != null) text.Text = BuildStatusHint();
    }


    private void WireStatusHint()
    {
        var left = this.FindControl<StackPanel>("StatusLeft");
        var hint = this.FindControl<Border>("StatusHint");
        if (left == null || hint == null) return;

        left.PointerEntered += (_, _) =>
        {
            var text = this.FindControl<TextBlock>("StatusHintText");
            if (text != null) text.Text = BuildStatusHint();
            hint.IsVisible = true;
        };
        left.PointerExited += (_, _) => hint.IsVisible = false;
    }


    private static string IdleDots(int tick) => (tick % 4) switch
    {
        0 => "^~^",
        1 => "^o^",
        2 => "^_^",
        _ => "^-^"
    };



    private static string ReadyShrink(int tick)
    {
        var frames = new[] { "ready", "read", "rea", "re", "r", "i", "i" };
        return frames[(tick / 3) % frames.Length].PadLeft(5);
    }


    private static string NoticeDots(int tick) => ((tick / 4) % 7) switch
    {
        0 => ">~<",
        1 => ">o<",
        2 => "._.",
        3 => "^~^",
        4 => "^o^",
        5 => "^_^",
        _ => "^-^"
    };


    private static string QuestionDots(int tick) => ((tick / 3) % 3) switch
    {
        0 => "?? ",
        1 => "???",
        _ => "?  "
    };

    private static Color HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }



    private void AnimateUpdateNotice(TextBlock dots)
    {
        var fileLabel = this.FindControl<TextBlock>("StatusFileLabel");
        var fileDot = this.FindControl<Border>("StatusFileDot");
        var stage = _updateService.Stage;


        if (stage != UpdateStage.Downloaded) { _doneTicks = 0; _doneExpired = false; }

        switch (stage)
        {
            case UpdateStage.Available:


                dots.Text = NoticeDots(_statusTick);
                if (fileLabel != null)
                {
                    fileLabel.Text = "ready";
                    fileLabel.IsVisible = true;
                }
                return;

            case UpdateStage.Downloading:

                if (fileDot != null)
                    fileDot.Background = new SolidColorBrush(HsvToRgb((_statusTick * 7) % 360, 0.7, 0.95));
                if (fileLabel != null)
                {
                    fileLabel.Text = $"downloading {_updateService.Progress}%";
                    fileLabel.IsVisible = true;
                }
                dots.Text = QuestionDots(_statusTick);
                return;

            case UpdateStage.Downloaded when !_doneExpired:

                if (string.IsNullOrEmpty(_doneCaption))
                    _doneCaption = new Random().Next(2) == 0 ? "how do I look っˆڡˆς" : "i well ripe っˆڡˆς";

                _doneTicks++;
                if (_doneTicks > 150) _doneExpired = true;
                else
                {
                    var blink = (_doneTicks / 3) % 2 == 0;
                    if (fileLabel != null)
                    {
                        // The caption with the kaomoji is longer than the fixed width — drop the
                        // width constraint, otherwise the text clips and the status below is hidden.
                        fileLabel.Width = double.NaN;
                        fileLabel.Text = blink ? _doneCaption : "ready";
                        fileLabel.IsVisible = true;
                    }
                    if (fileDot != null)
                        fileDot.Background = new SolidColorBrush(Color.Parse(blink ? "#34d399" : "#facc15"));
                    dots.Text = QuestionDots(_statusTick);
                }
                return;
        }

        dots.Text = IdleDots(_statusTick);
    }

    private void RefreshRustStatus()
    {
        try
        {
            var det = _steamService.DetectSteam();
            bool found = det.Success && !string.IsNullOrEmpty(det.RustPath);
            var logPath = _steamService.GetRustLogPath();
            _rustFullPath = found ? det.RustPath : null;
            if (!found) _rustPathExpanded = false;
            _rustMissing = !found;


            var rustText = this.FindControl<TextBlock>("RustStatusText");
            if (rustText != null)
            {
                rustText.IsVisible = found;
                rustText.Text = _rustPathExpanded ? _rustFullPath!
                    : $"rust in {AbbrevPath(_rustFullPath)}";
            }

            var rustDot = this.FindControl<Border>("RustStatusDot");
            if (rustDot != null)
            {
                rustDot.IsVisible = found;
                rustDot.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }



            string? relReal = ActiveWorkFile switch
            {
                "cfg/keys.txt" => "cfg/keys.cfg",
                "cfg/client.txt" => "cfg/client.cfg",
                _ => _workingFile
            };
            string? targetPath = relReal == "output_log.txt"
                ? logPath
                : found ? Path.Combine(det.RustPath, relReal.Replace('/', Path.DirectorySeparatorChar)) : null;
            _targetOk = targetPath != null && File.Exists(targetPath);

            var dotsUi = this.FindControl<TextBlock>("StatusDots");
            var fileLabel = this.FindControl<TextBlock>("StatusFileLabel");
            var fileDot = this.FindControl<Border>("StatusFileDot");
            var filePath = this.FindControl<TextBlock>("StatusFilePath");

            if (!found)
            {

                if (fileLabel != null)
                {
                    fileLabel.Text = "rust dont found :(";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {
                    fileDot.Background = new SolidColorBrush(Color.Parse("#ef4444"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = false;
                if (filePath != null)
                {
                    filePath.IsVisible = false;
                    filePath.Text = "";
                }
            }
            else if (!_workStarted)
            {

                if (fileLabel != null)
                {
                    fileLabel.Text = "ready";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {
                    fileDot.Background = new SolidColorBrush(Color.Parse("#facc15"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = true;
                if (filePath != null)
                {
                    filePath.IsVisible = false;
                    filePath.Text = "";
                }
            }
            else
            {
                if (fileLabel != null)
                {
                    fileLabel.Text = _targetOk ? "working" : "waiting";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {

                    fileDot.Background = new SolidColorBrush(Color.Parse(_targetOk ? "#22c55e" : "#facc15"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = true;
                if (filePath != null)
                {
                    filePath.IsVisible = true;
                    filePath.Text = $"with {ActiveWorkFile} on rust directory";
                }
            }
        }
        catch { }
    }

    /// <summary>



    /// </summary>
    private static string AbbrevPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var segs = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return path;


        int keepFrom = -1;
        for (int i = segs.Length - 1; i >= 0; i--)
            if (segs[i].Equals("rust", StringComparison.OrdinalIgnoreCase)) { keepFrom = i; break; }
        if (keepFrom < 0)
            for (int i = segs.Length - 1; i >= 0; i--)
                if (segs[i].IndexOf("rust", StringComparison.OrdinalIgnoreCase) >= 0) { keepFrom = i; break; }
        if (keepFrom < 0) keepFrom = Math.Max(0, segs.Length - 2);

        var sb = new System.Text.StringBuilder();
        int groups = Math.Min(keepFrom, 3);
        for (int i = 0; i < groups; i++) sb.Append("..../");
        for (int i = keepFrom; i < segs.Length; i++)
        {
            sb.Append(segs[i]);
            if (i < segs.Length - 1) sb.Append("/");
        }
        return sb.ToString();
    }




    private bool IsBusy => _trackerService.IsMonitoring || _cfgBusyFile != null;


    private string ActiveWorkFile => _cfgBusyFile ?? _workingFile;



    public void SetConfigBusy(string? file)
    {
        _cfgBusyFile = file;
        RefreshRustStatus();
    }


    private void SetWorkingFile(string file)
    {
        if (_workingFile != file) _rustPathExpanded = false;
        _workingFile = file;
        RefreshRustStatus();
    }


    private void OnRustStatusClick(object? sender, PointerPressedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rustFullPath)) return;
        _rustPathExpanded = !_rustPathExpanded;
        var tb = this.FindControl<TextBlock>("RustStatusText");
        if (tb != null)
            tb.Text = _rustPathExpanded ? _rustFullPath! : $"rust in {AbbrevPath(_rustFullPath)}";
    }

    private void StartOverlay()
    {
        _overlayReady = new System.Threading.ManualResetEventSlim(false);
        _overlayThread = new System.Threading.Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                _overlayWindow = new OverlayWindow(_settings);
                _overlayDispatcher = _overlayWindow.Dispatcher;
                _overlayReady.Set();
                _overlayWindow.Show();
                app.Run();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Overlay] Failed to start: {ex.Message}");
                try
                {
                    var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay_debug.log");
                    System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [Overlay] FAILED: {ex}\n");
                }
                catch { }
                _overlayReady.Set();
            }
        });
        _overlayThread.SetApartmentState(System.Threading.ApartmentState.STA);
        _overlayThread.IsBackground = true;
        _overlayThread.Start();
    }

    #endregion

    #region Settings

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _statusTimer?.Stop();
        _statusTimer = null;
        base.OnClosing(e);
    }

    private void ResetAll()
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI", "settings.json");
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            _settings.Load();
            ApplyTheme("black");
            SetStatus("All settings reset");
        }
        catch { }
    }

    #endregion

    #region Status

    private void SetStatus(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tb = this.FindControl<TextBlock>("BarStatusText");
            if (tb != null) tb.Text = text;
        });
    }

    #endregion
}
