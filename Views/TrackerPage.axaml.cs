using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        // Loaded fires again if the page is ever re-attached; wiring twice
        // would stack duplicate click handlers (one click = two toggles of
        // the parser - the "I have to press RUN twice" symptom).
        if (!_serviceWired)
        {
            _serviceWired = true;
            _trackerService.OnDataUpdated += OnDataUpdated;
            _trackerService.OnStatusChanged += OnStatusChanged;
            _trackerService.OnParseFinished += OnParseFinished;
        }

        WireParseModeStrip();
        WireSplitter();

        // A fresh page must reflect a parser that is already running - the
        // service outlives page instances across navigation.
        _isMonitoring = _trackerService.IsMonitoring;
        SyncRunButton();

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

        var filterBox = this.FindControl<TextBox>("TrackerFilter");
        if (filterBox != null) filterBox.TextChanged += (_, _) => ApplyFilter(filterBox.Text);

        RenderCustomSitesBar();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Navigation replaces the page instance while the service outlives it;
        // ghost subscriptions would run forever and slow every parse tick.
        _serviceWired = false;
        _trackerService.OnDataUpdated -= OnDataUpdated;
        _trackerService.OnStatusChanged -= OnStatusChanged;
        _trackerService.OnParseFinished -= OnParseFinished;
    }

    private void ApplyFilter(string? text)
    {
        _filter = text?.Trim().ToLowerInvariant() ?? "";
        RenderServers(_lastServers);
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

            _splitRatio = Math.Clamp(e.GetPosition(grid).Y / height, 0.15, 0.85);
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
        if (_topCollapsed)
        {

            rows[1].Height = GridLength.Auto;
            rows[3].Height = new GridLength(1, GridUnitType.Star);
        }
        else if (_bottomCollapsed)
        {
            rows[1].Height = new GridLength(1, GridUnitType.Star);
            rows[3].Height = GridLength.Auto;
        }
        else
        {
            rows[1].Height = new GridLength(_splitRatio, GridUnitType.Star);
            rows[3].Height = new GridLength(1 - _splitRatio, GridUnitType.Star);
        }

        var handle = this.FindControl<Border>("SplitHandle");
        if (handle != null) handle.IsVisible = !_topCollapsed && !_bottomCollapsed;


        SetArrow("KillMeCollapseBtn", _topCollapsed ? "▼" : "▲");
        SetArrow("CombatLogCollapseBtn", _bottomCollapsed ? "▲" : "▼");
    }

    private void SetArrow(string name, string glyph)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Content = glyph;
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


    private void RenderCombatLog()
    {
        var text = this.FindControl<TextBlock>("CombatLogText");
        if (text == null) return;

        var entries = _trackerService.CombatLog;
        text.Inlines ??= new InlineCollection();

        if (entries.Count == 0)
        {
            text.Inlines.Clear();
            text.Inlines.Add(new Run("combatlog events will be decoded here")
            {
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66"))
            });
            return;
        }


        text.Inlines.Clear();

        // Oldest first (Seconds ascending, stable so hits inside a fight keep
        // their order), each row numbered by its hit-series.
        var rows = new List<(string Main, string Block, string Area, bool Head, CombatLogEntry E)>();
        var hit = 0;
        var prevSec = double.MaxValue;
        foreach (var e in entries.OrderBy(x => x.Seconds))
        {
            // A pause longer than a minute — or a new session — starts a new series.
            hit = e.Seconds - prevSec > 60.0 || e.Seconds < prevSec ? 1 : hit + 1;
            prevSec = e.Seconds;

            var weapon = string.IsNullOrEmpty(e.Weapon) || e.Weapon == "N/A" ? "" : " with " + e.Weapon;
            var badge = e.IsKill ? " KILL" : e.IsDown ? " DOWN" : "";
            var newHp = string.IsNullOrEmpty(e.NewHp) ? "" : " " + FormatHp(e.NewHp);
            var main = $"({hit}) {e.Attacker}{weapon} > {e.Target}{newHp}{badge}";

            var hpBlock = !string.IsNullOrEmpty(e.OldHp) && !string.IsNullOrEmpty(e.NewHp)
                ? $"{FormatHp(e.OldHp)}>{FormatHp(e.NewHp)}"
                : "";
            var dist = FormatDistance(e.Distance);
            var block = hpBlock.Length > 0 && dist.Length > 0 ? hpBlock + "/" + dist
                        : hpBlock.Length > 0 ? hpBlock
                        : dist;

            var isHead = e.Area.Equals("head", StringComparison.OrdinalIgnoreCase);
            var area = e.Area == "N/A" ? "" : e.Area;
            if (e.IsKill || isHead) area = area.ToUpperInvariant();

            rows.Add((main, block, area, isHead, e));
        }

        // Fixed columns: every row pads its main part so the block starts at
        // one offset, with a label above the block.
        var mainWidth = Math.Max(rows.Max(r => r.Main.Length), 30);
        var blockWidth = rows.Max(r => r.Block.Length);

        text.Inlines.Add(new Run(new string(' ', mainWidth + 2) + "OLD>NEW/DIST")
        {
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66"))
        });
        text.Inlines.Add(new Run("\n"));

        foreach (var r in rows)
        {
            var main = r.Main.PadRight(mainWidth + 2);
            var block = r.Block.PadRight(blockWidth + 1);
            var area = r.Area.Length > 0 ? " " + r.Area : "";
            text.Inlines.Add(new Run(main + block + area + "\n")
            {
                // Headshots carry their own system colors: mine are dark green,
                // the enemy's are dark red.
                Foreground = new SolidColorBrush(Color.Parse(
                    r.Head && r.E.Outgoing ? "#16a34a"
                    : r.Head && r.E.Incoming ? "#b91c1c"
                    : r.E.IsKill ? "#f87171"
                    : r.E.Incoming ? "#e8737a"
                    : r.E.SelfInflicted ? "#f59e0b"
                    : r.E.Outgoing ? "#4ade80"
                    : "#5c5c66"))
            });
        }

        var scroll = this.FindControl<ScrollViewer>("CombatLogScroll");
        scroll?.ScrollToEnd();
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
        if (panel == null) return;
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

                var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                targetRow.Children.Add(new TextBlock
                {
                    Text = $"{target.PlayerName} — Killed me {target.TimesKilledMe}x",
                    Foreground = new SolidColorBrush(Color.Parse("#ededef")),
                    FontSize = 11
                });
                if (!string.IsNullOrEmpty(target.SteamId))
                {
                    targetRow.Children.Add(CreateCopyableId(target.SteamId));
                    targetRow.Children.Add(CreateSteamButton(target.PlayerName, target.SteamId));
                }
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
                        deathRow.Children.Add(CreateSteamButton(death.KillerName, death.KillerSteamId));
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

    private Button CreateSteamButton(string playerName, string steamId)
    {
        var btn = new Button
        {
            Content = "⛨",
            Height = 16,
            MinWidth = 24,
            Padding = new Thickness(5, 0),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Classes = { "pill" },
            Opacity = 0.85
        };
        ToolTip.SetTip(btn, $"Open {playerName}'s Steam profile");
        btn.Click += (_, _) => OpenSteamProfile(steamId);
        return btn;
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
