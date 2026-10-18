using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VoidUI.Services;

namespace VoidUI.Views;

/// <summary>
/// Support dialog opened from the home page: Steam trade offer, Destream and
/// crypto addresses with generated QR codes. Lives outside HomePage on purpose,
/// the home screen only has to know how to open it.
/// </summary>
public static class DonateDialog
{
    private const string TradeLink = "https://steamcommunity.com/tradeoffer/new/?partner=1481327474&token=_tqcnrxD";
    private const string DestreamLink = "https://destream.net/live/zeuthes/donate";

    private static readonly Color PageBg = Color.Parse("#0c0c0e");
    private static readonly Color CardBg = Color.Parse("#18181c");
    private static readonly Color CardBorder = Color.Parse("#232329");
    private static readonly Color RowBg = Color.Parse("#111114");
    private static readonly Color RowBorder = Color.Parse("#1e1e24");
    private static readonly Color ChipBg = Color.Parse("#1a1a22");
    private static readonly Color BtnBg = Color.Parse("#1f1f24");
    private static readonly Color TextPrimary = Color.Parse("#ededef");
    private static readonly Color TextMuted = Color.Parse("#9d9da5");
    private static readonly Color TextDim = Color.Parse("#5c5c66");

    // symbol, network, address, accent colour, warning shown above the QR code
    private static readonly (string Sym, string Full, string Addr, string Accent, string Warning)[] CryptoMethods =
    {
        ("ETH", "Ethereum", "0x40Aec830E1f25ab9E93b189051110A0Fd4099f8C", "#627eea", "Send only ETH on Ethereum network"),
        ("SOL", "Solana", "7EcDhSYGxXyscszYEp35KHN8vvw3svAuLKTzXwCFLtV", "#9945ff", "Send only SOL on Solana network"),
        ("USDT", "TRC-20", "TN3W4H6rK2ce4vX9YnFQHwKENnHjoxb3m9", "#26a17b", "Send only USDT on TRC-20 network"),
        ("LTC", "Litecoin", "ltc1q5cj82n5q7v0n3rz7n2m2r3z6p5q4e8x7t9y0u1i", "#bfbbbb", "Send only LTC on Litecoin network"),
    };

    /// <summary>Opens the dialog modally above <paramref name="owner"/>.</summary>
    public static void Show(Window? owner)
    {
        var win = new Window
        {
            Title = "Donate — Sweety Void",
            Width = 440, Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(PageBg),
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
        };

        var root = new Panel();
        AutomationProperties.SetAutomationId(win, "DonateWindow");

        // ═══ MAIN VIEW ═══
        var mainScroll = new ScrollViewer();
        var mainView = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };

        mainView.Children.Add(Caption("Support the project", 20, FontWeight.Bold, TextPrimary, true));
        mainView.Children.Add(Caption("Choose a method below to support Sweety Void", 12, FontWeight.Normal, TextMuted, true));
        mainView.Children.Add(BuildTradeCard(win));
        mainView.Children.Add(BuildDestreamCard());
        mainView.Children.Add(BuildCryptoCard(win, root, mainScroll));
        mainView.Children.Add(BuildCloseButton(win));

        mainScroll.Content = mainView;
        root.Children.Add(mainScroll);
        win.Content = root;
        win.ShowDialog(owner);
    }

    // ─── Steam trade card ───
    private static Border BuildTradeCard(Window win)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Caption("STEAM TRADE", 10, FontWeight.Bold, TextDim));
        stack.Children.Add(Caption("Send a trade offer for any skin", 13, FontWeight.SemiBold, TextPrimary));

        var copyBtn = Chip("Copy Trade Link", Color.Parse("#60a5fa"), "TradeCopyBtn");
        copyBtn.Click += (_, _) =>
        {
            try { TopLevel.GetTopLevel(win)?.Clipboard?.SetTextAsync(TradeLink); } catch { }
        };
        stack.Children.Add(copyBtn);

        return Card(stack);
    }

    // ─── Destream card ───
    private static Border BuildDestreamCard()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Caption("DESTREAM", 10, FontWeight.Bold, TextDim));
        stack.Children.Add(Caption("Donate via Destream", 13, FontWeight.SemiBold, TextPrimary));

        var openBtn = Chip("Open Destream →", Color.Parse("#a78bfa"), "DestreamBtn");
        openBtn.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(DestreamLink) { UseShellExecute = true });
        stack.Children.Add(openBtn);

        return Card(stack);
    }

    // ─── Crypto card: one row per network, click → QR detail ───
    private static Border BuildCryptoCard(Window win, Panel root, ScrollViewer mainScroll)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Caption("CRYPTO", 10, FontWeight.Bold, TextDim));

        foreach (var method in CryptoMethods)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(RowBg),
                BorderBrush = new SolidColorBrush(RowBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var accent = Color.Parse(method.Accent);
            var symBadge = new Border
            {
                Background = new SolidColorBrush(accent) { Opacity = 0.15 },
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = method.Sym,
                    FontSize = 11, FontWeight = FontWeight.ExtraBold,
                    Foreground = new SolidColorBrush(accent),
                },
            };

            var nameBlock = new TextBlock
            {
                Text = $"{method.Full} ({method.Sym})",
                FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };

            var arrow = new TextBlock
            {
                Text = "→", FontSize = 14,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetColumn(symBadge, 0);
            Grid.SetColumn(nameBlock, 1);
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(symBadge);
            grid.Children.Add(nameBlock);
            grid.Children.Add(arrow);

            // Transparent hit target: the row keeps its look but becomes a real
            // button, so UI automation can reach it.
            var hit = new Button
            {
                Content = grid,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(hit, "CryptoRow_" + method.Sym);

            var captured = method;
            hit.Click += (_, _) => ShowCryptoDetail(win, root, mainScroll, captured);

            row.Child = hit;
            stack.Children.Add(row);
        }

        return Card(stack);
    }

    // ─── Detail view: QR code, warning, address, copy ───
    private static void ShowCryptoDetail(Window win, Panel root, ScrollViewer mainScroll,
        (string Sym, string Full, string Addr, string Accent, string Warning) method)
    {
        mainScroll.IsVisible = false;

        var detailScroll = new ScrollViewer();
        var detail = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };

        var backBtn = SmallButton("← Back", "BackBtn");
        backBtn.Click += (_, _) =>
        {
            root.Children.Remove(detailScroll);
            mainScroll.IsVisible = true;
        };

        // Header badge
        var accent = Color.Parse(method.Accent);
        var badge = new Border
        {
            Background = new SolidColorBrush(accent) { Opacity = 0.15 },
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{method.Full} ({method.Sym})",
                FontSize = 14, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(accent),
            },
        };
        var header = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { badge },
        };

        // QR code
        var qrBorder = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8),
        };
        var qr = QrCodeService.Render(method.Addr);
        qrBorder.Child = qr != null
            ? new Image { Source = qr, Width = 160, Height = 160, Stretch = Stretch.Uniform }
            : new TextBlock { Text = "QR", Width = 160, Height = 160 };

        // Warning
        var warnBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2e2410")),
            BorderBrush = new SolidColorBrush(Color.Parse("#fbbf24")) { Opacity = 0.3 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = $"⚠ {method.Warning}",
                FontSize = 11, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#fbbf24")),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            },
        };

        // Address box
        var addrStack = new StackPanel { Spacing = 6 };
        addrStack.Children.Add(Caption("ADDRESS", 9, FontWeight.Bold, TextDim));
        addrStack.Children.Add(Caption(method.Addr, 11, FontWeight.SemiBold, TextPrimary));
        var addrBox = new Border
        {
            Background = new SolidColorBrush(RowBg),
            BorderBrush = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            Child = addrStack,
        };

        // Copy button
        var copyBtn = new Button
        {
            Content = "Copy Address",
            FontSize = 13, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            Background = new SolidColorBrush(accent),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(copyBtn, "CopyAddressBtn");
        copyBtn.Click += (_, _) =>
        {
            try { TopLevel.GetTopLevel(win)?.Clipboard?.SetTextAsync(method.Addr); } catch { }
            copyBtn.Content = "Copied!";
        };

        detail.Children.Add(backBtn);
        detail.Children.Add(header);
        detail.Children.Add(qrBorder);
        detail.Children.Add(warnBorder);
        detail.Children.Add(addrBox);
        detail.Children.Add(copyBtn);

        detailScroll.Content = detail;
        root.Children.Add(detailScroll);
    }

    // ─── Shared widgets ───

    private static Border Card(Panel content) => new Border
    {
        Background = new SolidColorBrush(CardBg),
        BorderBrush = new SolidColorBrush(CardBorder),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(20, 16),
        Child = content,
    };

    private static TextBlock Caption(string text, double size, FontWeight weight, Color color, bool center = false) =>
        new TextBlock
        {
            Text = text,
            FontSize = size, FontWeight = weight,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        };

    private static Button Chip(string text, Color fg, string automationId)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(fg),
            Background = new SolidColorBrush(ChipBg),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static Button SmallButton(string text, string automationId)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(TextMuted),
            Background = new SolidColorBrush(BtnBg),
            BorderBrush = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static Button BuildCloseButton(Window win)
    {
        var button = SmallButton("Close", "CloseDonateBtn");
        button.Padding = new Thickness(24, 8);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Click += (_, _) => win.Close();
        return button;
    }
}
