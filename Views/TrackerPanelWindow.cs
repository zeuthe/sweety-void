using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace VoidUI.Views;

/// <summary>
/// Floating window that hosts one tracker panel ("who killed me" or "combat
/// log") popped out of the Tracker page. Non-modal on purpose: the page keeps
/// rendering into <see cref="Body"/> while this window is open, so both views
/// stay in sync and the user can keep using the app.
/// </summary>
public class TrackerPanelWindow : Window
{
    private static readonly Color PageBg = Color.Parse("#0c0c0e");
    private static readonly Color TitleBg = Color.Parse("#111114");
    private static readonly Color BorderCol = Color.Parse("#232329");
    private static readonly Color TextPrimary = Color.Parse("#ededef");
    private static readonly Color TextDim = Color.Parse("#5c5c66");

    /// <summary>The panel the page renders rows into (same builder as inline).</summary>
    public StackPanel Body { get; } = new();

    private readonly ScrollViewer _scroll = new();
    private readonly Control? _header; // optional sticky table header (combat log)
    private readonly Border _titleBar;
    private readonly Border? _headerHost; // sticky header's wrapping border (combat log)

    // Background-off mode: remember the fills that were hidden so they can be
    // put back exactly as they were. Only Background changes — border lines
    // (BorderBrush) and text are never touched.
    private readonly Dictionary<Border, IBrush?> _rowBackgrounds = new();
    private bool _bgOff;

    public TrackerPanelWindow(string title, double width, double height, Control? header = null)
    {
        Title = $"Sweety Void — {title}";
        Width = width;
        Height = height;
        MinWidth = 320;
        MinHeight = 200;
        Background = new SolidColorBrush(PageBg);
        CanResize = true;
        SystemDecorations = SystemDecorations.None;
        // Always above every other window (main app included) — the user pops
        // the panel out to watch it while doing something else.
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetAutomationId(this, $"TrackerWindow{title.Replace(" ", "")}");

        _header = header;

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };

        // ── title bar: drag to move, close button ──
        var titleBar = new Border
        {
            Background = new SolidColorBrush(TitleBg),
            BorderBrush = new SolidColorBrush(BorderCol),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 8)
        };
        _titleBar = titleBar;
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titleText = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(TextDim),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(titleText, title);
        titleRow.Children.Add(titleText);
        Grid.SetColumn(titleText, 0);

        // Right side mirrors the browser mini-player title bar: the arrowed
        // twin of the page's plain pop glyph (traced from reference art).
        var btnCol = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var strokeDim = new SolidColorBrush(TextDim);
        var strokeHot = new SolidColorBrush(TextPrimary);
        // Return to the app: the arrowed twin of the page's plain pop glyph.
        var glyphPath = new Path
        {
            // Browser/mini-player style: the frame of a window with an arrow
            // coming back INTO it (twin of the page's pop-out glyph).
            Data = StreamGeometry.Parse("M14 4H6A2 2 0 0 0 4 6V18A2 2 0 0 0 6 20H18A2 2 0 0 0 20 18V12M21 3L12 12M12 6V12H18"),
            Stroke = strokeDim,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round
        };

        var returnBtn = new Button
        {
            Content = new Viewbox { Width = 22, Height = 22, Child = glyphPath },
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Transparent(),
            Foreground = strokeDim,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(returnBtn, "Return to the app");
        AutomationProperties.SetName(returnBtn, "Return to the app");
        void Hot(bool on)
        {
            var b = on ? strokeHot : strokeDim;
            glyphPath.Stroke = b;
            returnBtn.Foreground = b;
        }
        returnBtn.PointerEntered += (_, _) => Hot(true);
        returnBtn.PointerExited += (_, _) => Hot(false);
        returnBtn.Click += (_, _) => Close();
        btnCol.Children.Add(returnBtn);

        // Background toggle button: removes window background (only for this window)
        var bgBtn = new Button
        {
            Content = "⊘",
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            FontSize = 11,
            Background = Transparent(),
            Foreground = strokeDim,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(bgBtn, "Remove background");
        AutomationProperties.SetName(bgBtn, "Remove background");
        bgBtn.PointerEntered += (_, _) => bgBtn.Foreground = strokeHot;
        bgBtn.PointerExited += (_, _) => bgBtn.Foreground = strokeDim;
        // Every fill that gets hidden is remembered here, so switching back on
        // restores the original brushes (rows are re-rendered often).
        void HideFill(Border b)
        {
            if (b.Background == null || _rowBackgrounds.ContainsKey(b)) return;
            _rowBackgrounds[b] = b.Background;
            b.Background = Brushes.Transparent;
        }

        void HideRowBackgrounds()
        {
            foreach (var b in Body.GetVisualDescendants().OfType<Border>().ToList())
                HideFill(b);
            // Rows cleared from the panel are detached — drop their entries.
            foreach (var gone in _rowBackgrounds.Keys.Where(k => !k.IsAttachedToVisualTree()).ToList())
                _rowBackgrounds.Remove(gone);
        }

        void RestoreRowBackgrounds()
        {
            foreach (var kv in _rowBackgrounds) kv.Key.Background = kv.Value;
            _rowBackgrounds.Clear();
        }

        void ToggleBackground()
        {
            _bgOff = !_bgOff;
            if (_bgOff)
            {
                // Without the hint Windows keeps an opaque backdrop, so just
                // clearing Background does nothing visible.
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                Background = Transparent();
                _titleBar.Background = Transparent();
                if (_headerHost != null) _headerHost.Background = Transparent();
                HideRowBackgrounds();
            }
            else
            {
                RestoreRowBackgrounds();
                Background = new SolidColorBrush(PageBg);
                _titleBar.Background = new SolidColorBrush(TitleBg);
                if (_headerHost != null) _headerHost.Background = new SolidColorBrush(TitleBg);
                TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            }
            // Only fills change: border lines and text keep their brushes.
            bgBtn.Foreground = _bgOff ? strokeHot : strokeDim;
            bgBtn.Content = _bgOff ? "●" : "⊘";
        }

        bgBtn.Click += (_, _) => ToggleBackground();
        // The page re-renders Body often (parse ticks) — new rows must lose
        // their background too while the mode is on.
        Body.Children.CollectionChanged += (_, _) => { if (_bgOff) HideRowBackgrounds(); };
        btnCol.Children.Add(bgBtn);

        var btnHost = new Border
        {
            Child = btnCol,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        titleRow.Children.Add(btnHost);
        Grid.SetColumn(btnHost, 1);
        titleBar.Child = titleRow;

        // The whole strip moves the window (Avalonia handles it here, no Win32).
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        grid.Children.Add(titleBar);
        Grid.SetRow(titleBar, 0);

        if (header != null)
        {
            var headerHost = new Border
            {
                Background = new SolidColorBrush(TitleBg),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = header
            };
            _headerHost = headerHost;
            grid.Children.Add(headerHost);
            Grid.SetRow(headerHost, 1);
        }

        _scroll.Content = Body;
        grid.Children.Add(_scroll);
        Grid.SetRow(_scroll, 2);

        Content = grid;
    }

    /// <summary>Sticky table header visibility (combat log hides it when empty).</summary>
    public void SetHeaderVisible(bool visible)
    {
        if (_header != null) _header.IsVisible = visible;
    }

    public void ScrollToEnd() => _scroll.ScrollToEnd();

    private static IBrush Transparent() => new SolidColorBrush(Colors.Transparent);
}
