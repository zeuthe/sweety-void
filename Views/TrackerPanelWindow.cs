using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

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

        // Right side mirrors the browser mini-player: pop-out glyph (returns
        // the panel into the tracker page) + a close cross.
        var btnCol = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        var strokeDim = new SolidColorBrush(TextDim);
        var strokeHot = new SolidColorBrush(TextPrimary);
        var outline = new Path
        {
            Data = StreamGeometry.Parse("M3.8,2.75 H13.25 V13.25 H2.75 V6.6"),
            Stroke = strokeDim,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round
        };
        var shaft = new Path
        {
            Data = StreamGeometry.Parse("M1.9,1.9 L7.2,7.2"),
            Stroke = strokeDim,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round
        };
        var head = new Path
        {
            Data = StreamGeometry.Parse("M8.3,5.2 V8.3 H5.2 Z"),
            Fill = strokeDim
        };
        var corner = new Path
        {
            Data = StreamGeometry.Parse("M8.6,8.6 H13.25 V13.25 H8.6 Z"),
            Fill = strokeDim
        };
        var glyph = new Canvas { Width = 16, Height = 16 };
        glyph.Children.Add(outline);
        glyph.Children.Add(shaft);
        glyph.Children.Add(head);
        glyph.Children.Add(corner);

        var returnBtn = new Button
        {
            Content = new Viewbox { Width = 12, Height = 12, Child = glyph },
            Width = 22,
            Height = 22,
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
            outline.Stroke = b; shaft.Stroke = b; head.Fill = b; corner.Fill = b;
            returnBtn.Foreground = b;
        }
        returnBtn.PointerEntered += (_, _) => Hot(true);
        returnBtn.PointerExited += (_, _) => Hot(false);
        returnBtn.Click += (_, _) => Close();
        btnCol.Children.Add(returnBtn);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 9,
            Background = Transparent(),
            Foreground = strokeDim,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(closeBtn, "Close window");
        AutomationProperties.SetName(closeBtn, "Close window");
        closeBtn.PointerEntered += (_, _) =>
        {
            closeBtn.Foreground = strokeHot;
        };
        closeBtn.PointerExited += (_, _) =>
        {
            closeBtn.Foreground = strokeDim;
        };
        closeBtn.Click += (_, _) => Close();
        btnCol.Children.Add(closeBtn);

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
