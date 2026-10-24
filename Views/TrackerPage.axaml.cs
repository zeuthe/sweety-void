using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VoidUI.Models;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class TrackerPage : UserControl
{
    private readonly TrackerService _trackerService;
    private readonly SettingsService _settings;
    private readonly StringBuilder _logBuffer = new();
    private readonly List<TrackedSite> _customSites = new();
    private List<ServerInfo> _lastServers = new();
    private string? _lastFirstIp;
    private string _filter = "";
    private bool _isMonitoring;

    private string _activeMode = "";

    private bool _suppressPersist;

    private bool _serviceWired;
    private bool _popWired;

    // Popped-out panels: static, because these windows outlive page instances
    // across navigation — the window stays open while the page is swapped.
    private static TrackerPanelWindow? _killWindow;
    private static TrackerPanelWindow? _combatWindow;
    private static TrackerPage? _attached;   // the page instance currently on screen
    private static TrackerPage? _wiredOwner; // the single service subscriber among page instances

    private DateTime _lastRunClick = DateTime.MinValue;

    public TrackerPage(TrackerService trackerService, SettingsService settings)
    {
        _trackerService = trackerService;
        _settings = settings;
        InitializeComponent();
        _customSites.AddRange(settings.GetSettings().Tracker.CustomSites ?? new List<TrackedSite>());
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _attached = this;
        // Loaded fires again if the page is ever re-attached; wiring twice
        // would stack duplicate click handlers (one click = two toggles of
        // the parser - the "I have to press RUN twice" symptom).
        // Popped-out windows outlive page instances, so subscription ownership
        // is transferred to the page that is on screen now.
        WireAsOwner();

        WireParseModeStrip();
        WireCombatTable();
        WireSplitter();

        // A fresh page must reflect a parser that is already running - the
        // service outlives page instances across navigation.
        _isMonitoring = _trackerService.IsMonitoring;
        SyncRunButton();

        // Permanent mode means "always parse": if the parser is not running
        // (app was restarted, or a one-shot finished), re-entering the page
        // resumes it instead of making the user press RUN again.
        var cs0 = _settings.GetSettings().Tracker;
        if (!_trackerService.IsMonitoring && cs0.ParseMode == TrackerService.ModePermanent)
        {
            _activeMode = cs0.ParseMode;
            _trackerService.StartMonitoring(cs0.ParseMode, IntervalSecondsTotal(), cs0.IntervalSpread);
            _isMonitoring = true;
            SyncRunButton();
        }

        // A fresh page must show what the service already knows instead of
        // waiting for the next parse tick — that wait felt like "the info is
        // lost and nothing parses until I press RUN".
        var servers = _trackerService.Servers.ToList();
        _lastServers = servers;
        RenderServers(servers);
        RenderCombatLog();
        // Re-apply pop-out state: this page instance may be new while the
        // windows opened by an older instance are still on screen.
        OnPopOutChanged();

        var clearLogBtn = this.FindControl<Button>("ClearLogBtn");
        if (clearLogBtn != null) clearLogBtn.Click += (_, _) =>
        {
            _logBuffer.Clear();
            UpdateLog("[TRACKER] Log cleared");
            _trackerService.ClearCombatLog();
            RenderCombatLog();
        };

        var exportBtn = this.FindControl<Button>("ExportLogBtn");
        if (exportBtn != null) exportBtn.Click += (_, _) => ExportLog();

        var bmBtn = this.FindControl<Button>("OpenBMBtn");
        if (bmBtn != null) bmBtn.Click += (_, _) => OpenBattleMetrics();

        var addSiteBtn = this.FindControl<Button>("AddSiteBtn");
        if (addSiteBtn != null) addSiteBtn.Click += (_, _) => ShowAddSiteDialog();

        if (!_popWired)
        {
            _popWired = true;
            var killPop = this.FindControl<Button>("KillMePopOutBtn");
            if (killPop != null) killPop.Click += (_, _) => OpenKillWindow();
            var combatPop = this.FindControl<Button>("CombatLogPopOutBtn");
            if (combatPop != null) combatPop.Click += (_, _) => OpenCombatWindow();
        }

        var filterBox = this.FindControl<TextBox>("TrackerFilter");
        if (filterBox != null) filterBox.TextChanged += (_, _) => ApplyFilter(filterBox.Text);

        RenderCustomSitesBar();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_attached == this) _attached = null;
        // Navigation replaces the page instance while the service outlives it;
        // ghost subscriptions would run forever and slow every parse tick.
        // Popped-out windows keep rendering after the page is gone, so the
        // subscription stays alive until the last one closes.
        if (_killWindow != null || _combatWindow != null) return;
        UnwireService();
    }

    /// <summary>Become the one instance that listens to the tracker service.
    /// Popped-out windows let an older detached instance stay subscribed, so a
    /// freshly opened page takes the role over instead of stacking a second
    /// subscription on top of the old one.</summary>
    private void WireAsOwner()
    {
        if (_wiredOwner == this) return;
        _wiredOwner?.UnwireService();
        _wiredOwner = this;
        _serviceWired = true;
        _trackerService.OnDataUpdated += OnDataUpdated;
        _trackerService.OnStatusChanged += OnStatusChanged;
        _trackerService.OnParseFinished += OnParseFinished;
    }

    private void UnwireService()
    {
        _serviceWired = false;
        if (_wiredOwner == this) _wiredOwner = null;
        _trackerService.OnDataUpdated -= OnDataUpdated;
        _trackerService.OnStatusChanged -= OnStatusChanged;
        _trackerService.OnParseFinished -= OnParseFinished;
    }

    private void ApplyFilter(string? text)
    {
        _filter = text?.Trim().ToLowerInvariant() ?? "";
        RenderServers(_lastServers);
    }

    // ── Popped-out panels ──
    // One window per panel, the ↗ in the panel header is a toggle: it moves
    // the panel into a separate window (the inline copy collapses to the
    // header) and clicking it again — or the window's ✕ — brings the panel
    // back into the app. Windows are topmost: they stay above every window.
    private bool _killPopBusy;
    private bool _combatPopBusy;

    private async void OpenKillWindow()
    {
        if (_killPopBusy) return;
        if (_killWindow != null)
        {
            _ = PlayPopSweepAsync("KillMePopSweep"); // feedback while it returns
            _killWindow.Close();
            return;
        }
        _killPopBusy = true;
        try { await PlayPopSweepAsync("KillMePopSweep"); }
        finally { _killPopBusy = false; }
        if (_killWindow != null) return;
        var win = new TrackerPanelWindow("Who killed me", 460, 560);
        _killWindow = win;
        win.Closed += (_, _) =>
        {
            _killWindow = null;
            _attached?.OnPopOutChanged();
            if (_attached == null && _combatWindow == null) _wiredOwner?.UnwireService();
        };
        ShowPanelWindow(win);
        RenderServersInto(win.Body, _lastServers);
        OnPopOutChanged();
    }

    private async void OpenCombatWindow()
    {
        if (_combatPopBusy) return;
        if (_combatWindow != null)
        {
            _ = PlayPopSweepAsync("CombatLogPopSweep"); // feedback while it returns
            _combatWindow.Close();
            return;
        }
        _combatPopBusy = true;
        try { await PlayPopSweepAsync("CombatLogPopSweep"); }
        finally { _combatPopBusy = false; }
        if (_combatWindow != null) return;
        var win = new TrackerPanelWindow("Combat log", 860, 420, BuildCombatTableHeader());
        _combatWindow = win;
        win.Closed += (_, _) =>
        {
            _combatWindow = null;
            _attached?.OnPopOutChanged();
            if (_attached == null && _killWindow == null) _wiredOwner?.UnwireService();
        };
        ShowPanelWindow(win);
        RenderCombatLogInto(_combatWindow);
        OnPopOutChanged();
    }

    /// <summary>Green edge sweep along the pop button's bottom edge
    /// (bottom-left → bottom-right), like the browser's mini-player activation.</summary>
    private async Task PlayPopSweepAsync(string sweepName)
    {
        if (this.FindControl<Border>(sweepName) is not { } sweep) return;
        sweep.Width = 0;
        sweep.IsVisible = true;
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(380),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Layoutable.WidthProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Layoutable.WidthProperty, 18d) }
                }
            }
        };
        await anim.RunAsync(sweep);
        sweep.IsVisible = false;
    }

    /// <summary>Pop-out state changed: re-apply the split (a popped panel
    /// collapses to its header, the space goes to the other one) and flip the
    /// ↗ tooltips between "open" and "return to the app".</summary>
    private void OnPopOutChanged()
    {
        SetPopTip("KillMePopOutBtn", _killWindow != null);
        SetPopTip("CombatLogPopOutBtn", _combatWindow != null);
        ApplySplit();
    }

    private void SetPopTip(string name, bool poppedOut)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        var text = poppedOut ? "Return the panel to the app" : "Open in a separate window";
        ToolTip.SetTip(btn, text);
        AutomationProperties.SetName(btn, text);
    }

    private void ShowPanelWindow(TrackerPanelWindow win)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null) win.Show(owner);
        else win.Show();
    }

    private static Grid BuildCombatTableHeader()
    {
        var header = new Grid { ColumnDefinitions = ParseCombatColumns() };
        var labels = new[]
        {
            ("HIT", 0, TextAlignment.Center), ("WHO", 1, TextAlignment.Left),
            ("WEAPON", 2, TextAlignment.Left), ("TARGET", 3, TextAlignment.Left),
            ("NEW HP", 4, TextAlignment.Right), ("DMG", 5, TextAlignment.Right),
            ("OLD", 6, TextAlignment.Right), (">", 7, TextAlignment.Center),
            ("NEW", 8, TextAlignment.Right), ("DIST", 9, TextAlignment.Right),
            ("AREA", 10, TextAlignment.Left)
        };
        foreach (var (text, col, align) in labels)
        {
            header.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                TextAlignment = align,
                HorizontalAlignment = align == TextAlignment.Left
                    ? HorizontalAlignment.Left : align == TextAlignment.Right
                    ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66"))
            });
            Grid.SetColumn(header.Children[^1], col);
        }
        return header;
    }

    private string ResolveSiteUrl(TrackedSite site)
    {
        var ip = _lastServers.FirstOrDefault()?.ServerIp;
        return site.UrlTemplate.Replace("{ip}", ip ?? "");
    }

    private void RenderCustomSitesBar()
    {
        var bar = this.FindControl<StackPanel>("CustomSitesBar");
        if (bar == null) return;
        bar.Children.Clear();
        foreach (var site in _customSites)
        {
            var url = ResolveSiteUrl(site);
            bar.Children.Add(CreateSitePill(site.Name, url, () => OpenUrl(url), () => RemoveSite(site)));
        }
    }

    private void OpenBattleMetrics()
    {
        var ip = _lastServers.FirstOrDefault()?.ServerIp;
        var url = string.IsNullOrWhiteSpace(ip)
            ? "https://www.battlemetrics.com/"
            : $"https://www.battlemetrics.com/servers/search?q={Uri.EscapeDataString(ip)}";
        OpenUrl(url);
    }


    private void WireParseModeStrip()
    {
        WireModeButton("ModeOnceBtn", TrackerService.ModeOnce);
        WireModeButton("ModeIntervalBtn", TrackerService.ModeInterval);
        WireModeButton("ModePermanentBtn", TrackerService.ModePermanent);

        var runBtn = this.FindControl<Button>("RunParseBtn");
        if (runBtn != null) runBtn.Click += (_, _) => OnRunParseClicked();

        var menuBtn = this.FindControl<Button>("ParseMenuBtn");
        if (menuBtn != null) menuBtn.Click += (_, _) => ToggleParseMenu();

        _suppressPersist = true;
        var cs = _settings.GetSettings().Tracker;
        SetIntervalText("IntervalHoursBox", cs.IntervalHours);
        SetIntervalText("IntervalMinutesBox", cs.IntervalMinutes);
        SetIntervalText("IntervalSecondsBox", cs.IntervalSeconds);
        SetIntervalText("IntervalSpreadBox", cs.IntervalSpread);
        _suppressPersist = false;

        foreach (var name in IntervalBoxNames)
        {
            var box = this.FindControl<TextBox>(name);
            if (box == null) continue;
            box.TextChanged += (_, _) => OnIntervalEdited();


            box.LostFocus += (_, _) => NormalizeIntervalBox(box);
        }

        SyncModeStrip();
    }


    private double _splitRatio = 0.5;
    private bool _topCollapsed, _bottomCollapsed, _splitDragging;


    private void WireSplitter()
    {
        var grid = this.FindControl<Grid>("SplitGrid");
        var handle = this.FindControl<Border>("SplitHandle");
        if (grid == null || handle == null) return;

        handle.PointerPressed += (_, e) =>
        {
            _splitDragging = true;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!_splitDragging) return;
            var height = grid.Bounds.Height;
            if (height <= 0) return;

            var ratio = Math.Clamp(e.GetPosition(grid).Y / height, 0.03, 0.97);
            // Dragging near the top edge collapses "who killed me" down to its
            // header so the combat log can sit flush against it; pulling the
            // handle back down restores the normal split.
            if (_topCollapsed)
            {
                if (ratio <= 0.08) return;
                _topCollapsed = false;
            }
            else if (ratio <= 0.06 && !_bottomCollapsed)
            {
                _topCollapsed = true;
                ApplySplit();
                return;
            }
            else if (_bottomCollapsed)
            {
                _bottomCollapsed = false;
            }
            _splitRatio = ratio;
            ApplySplit();
        };
        handle.PointerReleased += (_, e) =>
        {
            _splitDragging = false;
            e.Pointer.Capture(null);
        };

        var top = this.FindControl<Button>("KillMeCollapseBtn");
        if (top != null) top.Click += (_, _) =>
        {
            _topCollapsed = !_topCollapsed;
            _bottomCollapsed = false;
            ApplySplit();
        };

        var bottom = this.FindControl<Button>("CombatLogCollapseBtn");
        if (bottom != null) bottom.Click += (_, _) =>
        {
            _bottomCollapsed = !_bottomCollapsed;
            _topCollapsed = false;
            ApplySplit();
        };

        ApplySplit();
    }


    private void ApplySplit()
    {
        var grid = this.FindControl<Grid>("SplitGrid");
        if (grid == null || grid.RowDefinitions.Count < 4) return;

        var rows = grid.RowDefinitions;

        // A panel popped into its own window leaves only its header strip (with
        // the ↗ return button) behind — the space goes to the panel that is
        // still in the app.
        var killScroll = this.FindControl<ScrollViewer>("KillMeScroll");
        if (killScroll != null) killScroll.IsVisible = _killWindow == null;
        var combatTable = this.FindControl<Grid>("CombatTableGrid");
        if (combatTable != null) combatTable.IsVisible = _combatWindow == null;

        if (_killWindow != null)
        {
            rows[1].Height = new GridLength(SectionHeaderHeight("KillMeHeader"), GridUnitType.Pixel);
            rows[3].Height = new GridLength(1, GridUnitType.Star);
        }
        else if (_combatWindow != null)
        {
            rows[1].Height = new GridLength(1, GridUnitType.Star);
            rows[3].Height = new GridLength(SectionHeaderHeight("CombatLogHeader"), GridUnitType.Pixel);
        }
        else if (_topCollapsed)
        {
            // A pixel height (not Auto): the section keeps only its header and
            // cannot swallow the other panel with unbounded content height.
            rows[1].Height = new GridLength(SectionHeaderHeight("KillMeHeader"), GridUnitType.Pixel);
            rows[3].Height = new GridLength(1, GridUnitType.Star);
        }
        else if (_bottomCollapsed)
        {
            rows[1].Height = new GridLength(1, GridUnitType.Star);
            rows[3].Height = new GridLength(SectionHeaderHeight("CombatLogHeader"), GridUnitType.Pixel);
        }
        else
        {
            rows[1].Height = new GridLength(_splitRatio, GridUnitType.Star);
            rows[3].Height = new GridLength(1 - _splitRatio, GridUnitType.Star);
        }

        var handle = this.FindControl<Border>("SplitHandle");
        if (handle != null) handle.IsVisible =
            !_topCollapsed && !_bottomCollapsed && _killWindow == null && _combatWindow == null;


        SetArrow("KillMeCollapseBtn", _topCollapsed ? "▼" : "▲");
        SetArrow("CombatLogCollapseBtn", _bottomCollapsed ? "▲" : "▼");
    }

    private void SetArrow(string name, string glyph)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = glyph;
    }

    /// <summary>Height of a section header strip (for collapsed rows), with a
    /// sensible fallback before the first layout pass.</summary>
    private double SectionHeaderHeight(string name)
    {
        var border = this.FindControl<Border>(name);
        var h = border?.Bounds.Height ?? 0;
        return h > 0 ? h : 31;
    }

    private static readonly string[] IntervalBoxNames =
    {
        "IntervalHoursBox", "IntervalMinutesBox", "IntervalSecondsBox", "IntervalSpreadBox"
    };


    private static (int Min, int Max) IntervalLimits(string name) => name switch
    {
        "IntervalHoursBox" => (0, 23),
        "IntervalMinutesBox" => (0, 59),
        "IntervalSecondsBox" => (0, 59),
        "IntervalSpreadBox" => (0, 3600),
        _ => (0, int.MaxValue)
    };

    private void NormalizeIntervalBox(TextBox box)
    {
        var (min, max) = IntervalLimits(box.Name ?? "");
        if (!int.TryParse(box.Text?.Trim(), out var v)) v = min;
        v = Math.Clamp(v, min, max);

        box.Text = v.ToString();
    }

    private void WireModeButton(string name, string mode)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += (_, _) => SelectParseMode(mode);
    }

    private void SetIntervalText(string name, int value)
    {
        var box = this.FindControl<TextBox>(name);
        if (box != null) box.Text = value.ToString();
    }


    private void ReadInterval(out int hours, out int minutes, out int seconds, out int spread)
    {
        hours = ReadInt("IntervalHoursBox");
        minutes = ReadInt("IntervalMinutesBox");
        seconds = ReadInt("IntervalSecondsBox");
        spread = ReadInt("IntervalSpreadBox");
    }

    private int ReadInt(string name)
    {
        var (min, max) = IntervalLimits(name);
        var box = this.FindControl<TextBox>(name);
        if (!int.TryParse(box?.Text?.Trim(), out var v)) return min;
        return Math.Clamp(v, min, max);
    }


    private double IntervalSecondsTotal()
    {
        ReadInterval(out var h, out var m, out var s, out _);
        return Math.Max(1.0, h * 3600.0 + m * 60.0 + s);
    }

    private void OnIntervalEdited()
    {
        if (_suppressPersist) return;

        ReadInterval(out var h, out var m, out var s, out var spread);

        var cs = _settings.GetSettings().Tracker;
        cs.IntervalHours = h;
        cs.IntervalMinutes = m;
        cs.IntervalSeconds = s;
        cs.IntervalSpread = spread;
        _settings.Save(_settings.GetSettings());


        if (_isMonitoring && _activeMode == TrackerService.ModeInterval)
            _trackerService.UpdateInterval(IntervalSecondsTotal(), spread);
    }

    private void SelectParseMode(string mode)
    {
        var cs = _settings.GetSettings().Tracker;
        var changed = cs.ParseMode != mode;
        cs.ParseMode = mode;
        _settings.Save(_settings.GetSettings());

        // Restart only when the mode really changed - re-picking the active
        // mode used to bounce the parser (stop + start churn for nothing).
        if (changed && _trackerService.IsMonitoring)
        {
            _trackerService.StopMonitoring();
            _activeMode = mode;
            _trackerService.StartMonitoring(mode, IntervalSecondsTotal(), cs.IntervalSpread);
        }

        _isMonitoring = _trackerService.IsMonitoring;

        SyncModeStrip();
        SyncRunButton();
        CloseParseMenu();
    }


    private void OnRunParseClicked()
    {
        // Debounce: a physical double-click must toggle the parser once, not
        // twice (two toggles = start + instant stop = "it did not start").
        var now = DateTime.UtcNow;
        if ((now - _lastRunClick).TotalMilliseconds < 350) return;
        _lastRunClick = now;

        var cs = _settings.GetSettings().Tracker;
        var mode = string.IsNullOrEmpty(cs.ParseMode) ? TrackerService.ModeOnce : cs.ParseMode;

        // The service is the single source of truth for the run state; the old
        // local flag could drift out of sync and swallow the next click.
        if (_trackerService.IsMonitoring)
        {
            _trackerService.StopMonitoring();
            _activeMode = "";
        }
        else
        {
            _activeMode = mode;
            _trackerService.StartMonitoring(mode, IntervalSecondsTotal(), cs.IntervalSpread);
        }

        _isMonitoring = _trackerService.IsMonitoring;

        SyncModeStrip();
        SyncRunButton();
    }


    private void SyncRunButton()
    {
        var btn = this.FindControl<Button>("RunParseBtn");
        if (btn == null) return;
        btn.Content = _isMonitoring ? "STOP" : "RUN";
    }


    private void ToggleParseMenu()
    {
        var menu = this.FindControl<Border>("ParseMenu");
        if (menu == null) return;
        if (menu.Classes.Contains("open")) menu.Classes.Remove("open");
        else menu.Classes.Add("open");
    }

    private void CloseParseMenu()
    {
        this.FindControl<Border>("ParseMenu")?.Classes.Remove("open");
    }

    private void OnParseFinished(string mode)
    {
        Dispatcher.UIThread.Post(() =>
        {

            if (_trackerService.IsMonitoring) return;

            _isMonitoring = false;
            _activeMode = "";
            SyncModeStrip();
            SyncRunButton();
        });
    }


    private void SyncModeStrip()
    {
        var selected = _settings.GetSettings().Tracker.ParseMode;

        SetModeActive("ModeOnceBtn", selected == TrackerService.ModeOnce);
        SetModeActive("ModeIntervalBtn", selected == TrackerService.ModeInterval);
        SetModeActive("ModePermanentBtn", selected == TrackerService.ModePermanent);

        var panel = this.FindControl<StackPanel>("IntervalPanel");
        if (panel != null) panel.IsVisible = selected == TrackerService.ModeInterval;
    }

    private void SetModeActive(string name, bool active)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        if (active) btn.Classes.Add("active");
        else btn.Classes.Remove("active");
    }

    private void OnStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null) statusText.Text = status;
        });
    }

    private void OnDataUpdated(List<ServerInfo> servers)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastServers = servers;
            var firstIp = servers.FirstOrDefault()?.ServerIp;
            if (firstIp != _lastFirstIp)
            {
                _lastFirstIp = firstIp;
                RenderCustomSitesBar();
            }
            RenderServers(servers);
            RenderCombatLog();
        });
    }


    // One column spec for the header and every row keeps them aligned at any
    // window width: hit series (mine only, per target), who, weapon,
    // target(+KILL/DEAD/DOWN), target's new hp, damage, then the hp pair
    // split in place as old > new, distance, body area. The name columns keep
    // a minimum width so they never collapse into pure ellipsis.
    private const string CombatLogCols = "38,2*,2*,2*,44,46,40,14,40,46,58";
    private const double CombatLogNameMinWidth = 64;

    private void WireCombatTable()
    {
        var header = this.FindControl<Grid>("CombatLogHeaderGrid");
        if (header != null) header.ColumnDefinitions = ParseCombatColumns();
    }

    private static ColumnDefinitions ParseCombatColumns()
    {
        var defs = new ColumnDefinitions();
        foreach (var part in CombatLogCols.Split(','))
        {
            if (part.EndsWith("*"))
            {
                var star = new ColumnDefinition(
                    new GridLength(double.Parse(part.Substring(0, part.Length - 1),
                        System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Star));
                star.MinWidth = CombatLogNameMinWidth;
                defs.Add(star);
            }
            else
                defs.Add(new ColumnDefinition(
                    new GridLength(double.Parse(part,
                        System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Pixel)));
        }
        return defs;
    }

    private void RenderCombatLog()
    {
        var panel = this.FindControl<StackPanel>("CombatLogPanel");
        if (panel == null) return;
        var header = this.FindControl<Grid>("CombatLogHeaderGrid");

        var entries = _trackerService.CombatLog;
        panel.Children.Clear();

        bool empty = entries.Count == 0;
        if (header != null) header.IsVisible = !empty;
        FillCombatPanel(panel, entries);

        var scroll = this.FindControl<ScrollViewer>("CombatLogScroll");
        if (!empty) scroll?.ScrollToEnd();

        if (_combatWindow != null) RenderCombatLogInto(_combatWindow);
    }

    private void RenderCombatLogInto(TrackerPanelWindow win)
    {
        var entries = _trackerService.CombatLog;
        win.SetHeaderVisible(entries.Count > 0);
        win.Body.Children.Clear();
        FillCombatPanel(win.Body, entries);
        if (entries.Count > 0) win.ScrollToEnd();
    }

    private void FillCombatPanel(StackPanel panel, IReadOnlyList<CombatLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "combatlog events will be decoded here",
                FontFamily = CombatFont,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66"))
            });
            return;
        }

        // The hit counter is mine-only and per target: hitting patsik numbers
        // 1,2,3…, hitting bich starts its own 1,2,…. A pause longer than a
        // minute — or a new session — restarts that player's series.
        var hits = new Dictionary<string, (int Count, double LastSec)>();

        foreach (var e in entries.OrderBy(x => x.Seconds))
        {
            var isHead = e.Area.Equals("head", StringComparison.OrdinalIgnoreCase);
            var area = e.Area == "N/A" ? "" : e.Area;
            if (e.IsKill || isHead) area = area.ToUpperInvariant();

            var weapon = string.IsNullOrEmpty(e.Weapon) || e.Weapon == "N/A" ? "—" : e.Weapon;
            // My kill is KILL; every kill where I end up dead (someone's or my
            // own) is DEAD.
            var badge = e.IsKill ? (e.Outgoing ? " KILL" : " DEAD") : e.IsDown ? " DOWN" : "";

            var who = _trackerService.DisplayName(e.Attacker);
            var target = _trackerService.DisplayName(e.Target);

            string hitLabel;
            if (e.Outgoing)
            {
                var key = target.ToLowerInvariant();
                hits.TryGetValue(key, out var st);
                var count = e.Seconds - st.LastSec > 60.0 || e.Seconds < st.LastSec ? 1 : st.Count + 1;
                hits[key] = (count, e.Seconds);
                hitLabel = $"({count})";
            }
            else hitLabel = "—";

            var newHp = FormatHp(e.NewHp);

            // Headshots carry their own system colors: mine are dark green,
            // the enemy's are dark red. My headshot also lights the row up.
            var brush = new SolidColorBrush(Color.Parse(
                isHead && e.Outgoing ? "#16a34a"
                : isHead && e.Incoming ? "#b91c1c"
                : e.IsKill ? "#f87171"
                : e.Incoming ? "#e8737a"
                : e.SelfInflicted ? "#f59e0b"
                : e.Outgoing ? "#4ade80"
                : "#5c5c66"));
            var rowBg = isHead && e.Outgoing
                ? new SolidColorBrush(Color.Parse("#132b1a"))
                : null;

            panel.Children.Add(BuildCombatRow(hitLabel, who, weapon, target, badge,
                newHp, FormatDmg(e), FormatHp(e.OldHp), newHp,
                FormatDistance(e.Distance), area, brush, rowBg));
        }
    }

    private static Border BuildCombatRow(string hit, string who, string weapon, string target,
        string badge, string newHp, string dmg, string hpOld, string hpNew,
        string dist, string area, IBrush brush, IBrush? rowBg)
    {
        var grid = new Grid { ColumnDefinitions = ParseCombatColumns() };

        grid.Children.Add(CombatCell(hit, 0, brush, TextAlignment.Center));
        grid.Children.Add(CombatCell(who, 1, brush, TextAlignment.Left));
        grid.Children.Add(CombatCell(weapon, 2, brush, TextAlignment.Left));

        var targetCell = CombatCell("", 3, brush, TextAlignment.Left);
        targetCell.Inlines.Clear();
        targetCell.Inlines.Add(new Run(target));
        if (badge.Length > 0)
            targetCell.Inlines.Add(new Run(badge) { FontWeight = FontWeight.Bold });
        grid.Children.Add(targetCell);

        grid.Children.Add(CombatCell(newHp, 4, brush, TextAlignment.Right));

        var dmgCell = CombatCell(dmg, 5, brush, TextAlignment.Right);
        // A "+" damage value means the target healed between the hits.
        if (dmg.StartsWith("+", StringComparison.Ordinal))
            ToolTip.SetTip(dmgCell, "Apparently healed");
        grid.Children.Add(dmgCell);

        grid.Children.Add(CombatCell(hpOld, 6, brush, TextAlignment.Right));
        grid.Children.Add(CombatCell(
            !string.IsNullOrEmpty(hpOld) && !string.IsNullOrEmpty(hpNew) ? ">" : "",
            7, brush, TextAlignment.Center));
        grid.Children.Add(CombatCell(hpNew, 8, brush, TextAlignment.Right));
        grid.Children.Add(CombatCell(dist, 9, brush, TextAlignment.Right));
        grid.Children.Add(CombatCell(area, 10, brush, TextAlignment.Left));

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#1a1a1f")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = rowBg,
            Child = grid
        };
    }

    private static readonly FontFamily CombatFont = new("Cascadia Code, Consolas, monospace");

    private static TextBlock CombatCell(string text, int col, IBrush brush, TextAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = CombatFont,
            FontSize = 10,
            TextAlignment = align,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, align == TextAlignment.Right ? 8 : 6, 1)
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static string FormatDmg(CombatLogEntry e)
    {
        if (!double.TryParse(e.OldHp, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var o) ||
            !double.TryParse(e.NewHp, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return "";
        var d = (int)Math.Round(o - n);
        return d > 0 ? "-" + d : d < 0 ? "+" + (-d) : "0";
    }


    private static string FormatHp(string hp)
    {
        if (string.IsNullOrWhiteSpace(hp)) return "";
        return double.TryParse(hp, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? ((int)Math.Round(v)).ToString()
            : hp;
    }

    private static string FormatDistance(string distance)
    {
        if (string.IsNullOrWhiteSpace(distance) || distance == "N/A") return "";
        var t = distance.EndsWith("m", StringComparison.OrdinalIgnoreCase)
            ? distance.Substring(0, distance.Length - 1)
            : distance;
        return double.TryParse(t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? ((int)Math.Round(v)) + "m"
            : distance;
    }

    private void RenderServers(List<ServerInfo> servers)
    {
        var panel = this.FindControl<StackPanel>("ServersPanel");
        if (panel != null) RenderServersInto(panel, servers);
        if (_killWindow != null) RenderServersInto(_killWindow.Body, servers);

        _logBuffer.Clear();
        foreach (var server in servers)
        {
            _logBuffer.AppendLine($"[SERVER] {server.ServerName} ({server.ServerIp})");




            foreach (var death in server.Targets
                         .SelectMany(t => t.DeathHistory)
                         .OrderByDescending(d => d.Timestamp)
                         .Take(200))
            {
                var steamId = death.IsPlayer && !string.IsNullOrEmpty(death.KillerSteamId)
                    ? $" ({death.KillerSteamId})"
                    : "";
                _logBuffer.AppendLine($"  [{death.Timestamp}] Killed by {death.KillerName}{steamId}");
            }
        }
        if (_logBuffer.Length > 0) UpdateLog(_logBuffer.ToString());
    }

    private void RenderServersInto(StackPanel panel, List<ServerInfo> servers)
    {
        panel.Children.Clear();

        bool hasFilter = _filter.Length > 0;
        int shownServers = 0;

        foreach (var server in servers)
        {
            bool serverMatches = !hasFilter
                || server.ServerName.ToLowerInvariant().Contains(_filter)
                || server.ServerIp.ToLowerInvariant().Contains(_filter);

            List<TargetPlayer> targets = (!hasFilter || serverMatches)
                ? server.Targets
                : server.Targets.Where(t => TargetMatches(t, _filter)).ToList();

            if (hasFilter && !serverMatches && targets.Count == 0) continue;
            shownServers++;

            var serverBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#111114")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1a1a1f")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10)
            };
            var sp = new StackPanel { Spacing = 4 };

            var serverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var serverInfo = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            serverInfo.Children.Add(new TextBlock { Text = server.ServerName, Foreground = new SolidColorBrush(Color.Parse("#ededef")), FontSize = 13, FontWeight = FontWeight.SemiBold });
            serverInfo.Children.Add(new TextBlock { Text = server.ServerIp, Foreground = new SolidColorBrush(Color.Parse("#5c5c66")), FontSize = 10 });
serverRow.Children.Add(serverInfo);

            sp.Children.Add(serverRow);

            foreach (var target in targets.OrderByDescending(t => t.TimesKilledMe))
            {
                var targetSp = new StackPanel { Spacing = 2, Margin = new Thickness(12, 4, 0, 0) };

                var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                targetRow.VerticalAlignment = VerticalAlignment.Center;
                if (!string.IsNullOrEmpty(target.SteamId))
                    targetRow.Children.Add(CreateAvatar(target.PlayerName, target.SteamId, target.AvatarUrl));
                targetRow.Children.Add(new TextBlock
                {
                    Text = $"{target.PlayerName} — Killed me {target.TimesKilledMe}x",
                    Foreground = new SolidColorBrush(Color.Parse("#ededef")),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (!string.IsNullOrEmpty(target.SteamId))
                    targetRow.Children.Add(CreateCopyableId(target.SteamId));
                targetSp.Children.Add(targetRow);

                foreach (var death in target.DeathHistory.TakeLast(3))
                {
                    var deathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    deathRow.Children.Add(new TextBlock
                    {
                        Text = $"  [{death.Timestamp}] {death.KillerName}",
                        Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
                        FontSize = 10,
                        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace")
                    });
                    if (death.IsPlayer && !string.IsNullOrEmpty(death.KillerSteamId))
                    {
                        deathRow.Children.Add(CreateCopyableId(death.KillerSteamId));
                    }
                    targetSp.Children.Add(deathRow);
                }
                sp.Children.Add(targetSp);
            }
            serverBorder.Child = sp;
            panel.Children.Add(serverBorder);
        }

        if (hasFilter && shownServers == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No matches",
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
                FontSize = 12,
                Margin = new Thickness(16, 14)
            });
        }
    }

    private static bool TargetMatches(TargetPlayer target, string filter)
    {
        if (target.PlayerName.ToLowerInvariant().Contains(filter)) return true;
        if (!string.IsNullOrEmpty(target.SteamId) && target.SteamId.Contains(filter)) return true;
        return target.DeathHistory.Any(d =>
            (!string.IsNullOrEmpty(d.KillerName) && d.KillerName.ToLowerInvariant().Contains(filter)) ||
            (!string.IsNullOrEmpty(d.KillerSteamId) && d.KillerSteamId.Contains(filter)));
    }

    private Button CreateSiteButton(string label, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 22,
            MinWidth = 0,
            Padding = new Thickness(8, 0),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Classes = { "pill" }
        };
        ToolTip.SetTip(btn, tooltip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Border CreateSitePill(string name, string tooltip, Action onOpen, Action onRemove)
    {
        var label = new TextBlock
        {
            Text = name,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        label.PointerPressed += (_, _) => onOpen();

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 14,
            Height = 14,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
            FontSize = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        removeBtn.Click += (_, _) => onRemove();

        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        inner.Children.Add(label);
        inner.Children.Add(removeBtn);

        var pill = new Border
        {
            Child = inner,
            Height = 22,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#1e1e26")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(pill, $"{tooltip} (click ✕ to remove)");
        return pill;
    }

    private void RemoveSite(TrackedSite site)
    {
        _customSites.Remove(site);
        var settings = _settings.GetSettings();
        settings.Tracker.CustomSites = new List<TrackedSite>(_customSites);
        _settings.Save(settings);
        RenderCustomSitesBar();
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ShowAddSiteDialog()
    {
        var win = new Window
        {
            Width = 360,
            Height = 236,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        win.Resources["TextControlForegroundPlaceholder"] = new SolidColorBrush(Color.Parse("#5c5c66"));
        win.Resources["TextBoxForegroundPlaceholder"] = new SolidColorBrush(Color.Parse("#5c5c66"));

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
        };

        void StyleTextBox(TextBox tb)
        {
            tb.Background = new SolidColorBrush(Color.Parse("#111114"));
            tb.BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34"));
            tb.BorderThickness = new Thickness(1);
            tb.CornerRadius = new CornerRadius(8);
            tb.Foreground = new SolidColorBrush(Color.Parse("#ededef"));
            tb.Padding = new Thickness(10, 0);
            tb.VerticalContentAlignment = VerticalAlignment.Center;
        }

        var nameBox = new TextBox { Watermark = "Site name (e.g. RustCheaters)", Height = 32, FontSize = 12 };
        var urlBox = new TextBox { Watermark = "URL — use {ip} for server IP", Height = 32, FontSize = 12, Text = "https://" };
        StyleTextBox(nameBox);
        StyleTextBox(urlBox);

        var title = new TextBlock
        {
            Text = "Add custom server tracker",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        closeBtn.Click += (_, _) => win.Close();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(title, 0);
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(title);
        header.Children.Add(closeBtn);

        var addBtn = new Button
        {
            Content = "Add",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#1e1e26")),
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(addBtn);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(header);
        panel.Children.Add(nameBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;

        void Confirm()
        {
            AddSite(nameBox.Text?.Trim() ?? "", urlBox.Text?.Trim() ?? "");
            win.Close();
        }

        addBtn.Click += (_, _) => Confirm();
        cancelBtn.Click += (_, _) => win.Close();
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
            else if (e.Key == Key.Enter) Confirm();
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
            win.ShowDialog(owner);
        else
            win.Show();
    }

    private void AddSite(string name, string urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(urlTemplate)) return;
        _customSites.Add(new TrackedSite { Name = name, UrlTemplate = urlTemplate });

        var settings = _settings.GetSettings();
        settings.Tracker.CustomSites = new List<TrackedSite>(_customSites);
        _settings.Save(settings);

        RenderCustomSitesBar();
    }

    private TextBlock CreateCopyableId(string steamId)
    {
        var tb = new TextBlock
        {
            Text = steamId,
            Foreground = new SolidColorBrush(Color.Parse("#7c5cfc")),
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(tb, $"Copy Steam ID: {steamId}");
        tb.PointerPressed += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(tb);
            if (top?.Clipboard == null) return;
            await top.Clipboard.SetTextAsync(steamId);
            tb.Text = "copied ✓";
            await Task.Delay(900);
            tb.Text = steamId;
        };
        return tb;
    }

    // Avatars replace the old Steam button: click opens the profile, and only
    // real players (entries with a SteamId) get one — NPCs like "Drowned" do not.
    private static readonly System.Net.Http.HttpClient AvatarHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _avatarBitmaps = new();
    private readonly HashSet<string> _avatarTried = new();

    private Border CreateAvatar(string playerName, string steamId, string avatarUrl)
    {
        var border = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#1c1c22")),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        if (_avatarBitmaps.TryGetValue(steamId, out var bmp))
        {
            border.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        }
        else
        {
            // Placeholder letter until the image arrives.
            border.Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(playerName) ? "?" : playerName.Substring(0, 1).ToUpperInvariant(),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.Parse("#7c5cfc")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(avatarUrl) && _avatarTried.Add(steamId))
                _ = LoadAvatarAsync(steamId, avatarUrl);
        }

        ToolTip.SetTip(border, $"Open {playerName}'s Steam profile");
        border.PointerPressed += (_, _) => OpenSteamProfile(steamId);
        return border;
    }

    private async Task LoadAvatarAsync(string steamId, string url)
    {
        try
        {
            var bytes = await AvatarHttp.GetByteArrayAsync(url);
            using var stream = new System.IO.MemoryStream(bytes);
            var bmp = new Avalonia.Media.Imaging.Bitmap(stream);
            _avatarBitmaps[steamId] = bmp;
            // Re-render picks the loaded image up; only this one changed.
            Dispatcher.UIThread.Post(() => RenderServers(_lastServers));
        }
        catch { _avatarTried.Remove(steamId); /* retry on the next render */ }
    }

    private void OpenSteamProfile(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return;
        var url = $"https://steamcommunity.com/profiles/{steamId}/";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"steam://openurl/{url}")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
    }

    private void UpdateLog(string text)
    {
        var logText = this.FindControl<TextBlock>("LogText");
        if (logText != null) logText.Text = text;
        var scroll = this.FindControl<ScrollViewer>("LogScroll");
        scroll?.ScrollToEnd();
    }

    private void ExportLog()
    {
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"tracker_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        System.IO.File.WriteAllText(path, _logBuffer.ToString());
    }
}
