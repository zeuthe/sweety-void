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

        // "Return to the app": text button (a down-arrow icon read poorly here)
        // — closes the window and puts the panel back into the tracker page.
        var closeBtn = new Button
        {
            Content = "Return to app",
            Padding = new Thickness(10, 4),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Background = Transparent(),
            BorderBrush = new SolidColorBrush(BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Foreground = new SolidColorBrush(TextDim),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(closeBtn, "Return to the app");
        AutomationProperties.SetName(closeBtn, "Return to app");
        closeBtn.PointerEntered += (_, _) =>
        {
            closeBtn.Foreground = new SolidColorBrush(TextPrimary);
            closeBtn.Background = new SolidColorBrush(TitleBg);
        };
        closeBtn.PointerExited += (_, _) =>
        {
            closeBtn.Foreground = new SolidColorBrush(TextDim);
            closeBtn.Background = new SolidColorBrush(Colors.Transparent);
        };
        closeBtn.Click += (_, _) => Close();
        titleRow.Children.Add(closeBtn);
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
