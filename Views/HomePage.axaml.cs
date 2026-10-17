using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;

namespace VoidUI.Views;

public partial class HomePage : UserControl
{
    private readonly Action<string> _navigateTo;

    private static readonly string[] CandyColors =
    {
        "#ff698c", "#ff9a56", "#ffd93d", "#6bff6b",
        "#6bb5ff", "#b06bff", "#ff6bda", "#6bffe0"
    };

    // (width, height) — wrapped candy with twisted ends (SVG style)
    private static readonly (double W, double H)[] CandyTypes =
    {
        (44, 20), (36, 16), (48, 18), (40, 16), (52, 20), (36, 18),
    };

    public HomePage(Action<string> navigateTo)
    {
        _navigateTo = navigateTo;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private double _donutAngle;
    private DispatcherTimer? _donutTimer;

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WireCards();
        SpawnConfetti();
        StartDonutSpin();
    }

    private void StartDonutSpin()
    {
        var donutImage = this.FindControl<Image>("DonutImage");
        if (donutImage == null) return;

        _donutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _donutTimer.Tick += (_, _) =>
        {
            _donutAngle += 0.8;
            if (_donutAngle >= 360) _donutAngle -= 360;
            donutImage.RenderTransform = new RotateTransform(_donutAngle);
        };
        _donutTimer.Start();
    }

    private void WireCards()
    {
        var cardCrosshair = this.FindControl<Border>("CardCrosshair");
        var cardGraphics = this.FindControl<Border>("CardGraphics");
        var cardBinds = this.FindControl<Border>("CardBinds");
        var cardHelpers = this.FindControl<Border>("CardHelpers");
        var cardTracker = this.FindControl<Border>("CardTracker");
        var githubBtn = this.FindControl<Button>("GitHubBtn");
        var donateBtn = this.FindControl<Button>("DonateBtn");
        var settingsBtn = this.FindControl<Button>("SettingsBtn");

        if (cardCrosshair != null) cardCrosshair.PointerPressed += (_, _) => _navigateTo("crosshair");
        if (cardGraphics != null) cardGraphics.PointerPressed += (_, _) => _navigateTo("graphics");
        if (cardBinds != null) cardBinds.PointerPressed += (_, _) => _navigateTo("binds");
        if (cardHelpers != null) cardHelpers.PointerPressed += (_, _) => _navigateTo("helpers");
        if (cardTracker != null) cardTracker.PointerPressed += (_, _) => _navigateTo("tracker");
        if (githubBtn != null) githubBtn.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/zeuthe/void-ui") { UseShellExecute = true });
        if (donateBtn != null) donateBtn.Click += (_, _) => OpenDonateWindow();
        if (settingsBtn != null) settingsBtn.Click += (_, _) => _navigateTo("settings");
    }

    private void OpenDonateWindow()
    {
        var win = new Window
        {
            Title = "Donate — Sweety Void",
            Width = 440, Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#0c0c0e")),
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
        };

        var root = new Panel();

        // ═══ MAIN VIEW ═══
        var mainScroll = new ScrollViewer();
        var mainView = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };

        var title = new TextBlock
        {
            Text = "Support the project",
            FontSize = 20, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var subtitle = new TextBlock
        {
            Text = "Choose a method below to support Sweety Void",
            FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#9d9da5")),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };

        // ─── Steam Trade Card ───
        var tradeCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#18181c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
        };
        var tradeStack = new StackPanel { Spacing = 8 };
        tradeStack.Children.Add(new TextBlock
        {
            Text = "STEAM TRADE", FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
        });
        tradeStack.Children.Add(new TextBlock
        {
            Text = "Send a trade offer for any skin",
            FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
        });
        var tradeLink = "https://steamcommunity.com/tradeoffer/new/?partner=1481327474&token=_tqcnrxD";
        var tradeBtn = new Button
        {
            Content = "Copy Trade Link", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#60a5fa")),
            Background = new SolidColorBrush(Color.Parse("#1a1a22")),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        tradeBtn.Click += (_, _) =>
        {
            try { TopLevel.GetTopLevel(win)?.Clipboard?.SetTextAsync(tradeLink); } catch { }
        };
        tradeStack.Children.Add(tradeBtn);
        tradeCard.Child = tradeStack;

        // ─── Crypto Card ───
        var cryptoCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#18181c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
        };
        var cryptoStack = new StackPanel { Spacing = 10 };
        cryptoStack.Children.Add(new TextBlock
        {
            Text = "CRYPTO", FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
        });

        var cryptoMethods = new[]
        {
            ("ETH", "Ethereum", "0x40Aec830E1f25ab9E93b189051110A0Fd4099f8C", "eth-qr.png", "#627eea", "Send only ETH on Ethereum network"),
            ("SOL", "Solana", "7EcDhSYGxXyscszYEp35KHN8vvw3svAuLKTzXwCFLtV", "sol-qr.png", "#9945ff", "Send only SOL on Solana network"),
            ("USDT", "TRC-20", "TN3W4H6rK2ce4vX9YnFQHwKENnHjoxb3m9", "usdt-qr.png", "#26a17b", "Send only USDT on TRC-20 network"),
            ("LTC", "Litecoin", "ltc1q5cj82n5q7v0n3rz7n2m2r3z6p5q4e8x7t9y0u1i", "ltc-qr.png", "#bfbbbb", "Send only LTC on Litecoin network"),
        };

        foreach (var (sym, full, addr, qrFile, accent, warning) in cryptoMethods)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#111114")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1e1e24")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var symBadge = new Border
            {
                Background = new SolidColorBrush(Color.Parse(accent)) { Opacity = 0.15 },
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = sym, FontSize = 11, FontWeight = FontWeight.ExtraBold,
                    Foreground = new SolidColorBrush(Color.Parse(accent)),
                },
            };

            var nameBlock = new TextBlock
            {
                Text = $"{full} ({sym})", FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#ededef")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };

            var arrow = new TextBlock
            {
                Text = "→", FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            Grid.SetColumn(symBadge, 0);
            Grid.SetColumn(nameBlock, 1);
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(symBadge);
            grid.Children.Add(nameBlock);
            grid.Children.Add(arrow);
            row.Child = grid;

            // Click → open detail
            string captureAddr = addr, captureQr = qrFile, captureAccent = accent, captureWarning = warning, captureSym = sym, captureFull = full;
            row.PointerPressed += (_, _) => ShowCryptoDetail(win, root, mainScroll, mainView, captureSym, captureFull, captureAddr, captureQr, captureAccent, captureWarning);

            cryptoStack.Children.Add(row);
        }
        cryptoCard.Child = cryptoStack;

        // ─── Close ───
        var closeBtn = new Button
        {
            Content = "Close", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#9d9da5")),
            Background = new SolidColorBrush(Color.Parse("#1f1f24")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        closeBtn.Click += (_, _) => win.Close();

        // ─── Destream Card ───
        var destreamCard = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#18181c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 16),
        };
        var destreamStack = new StackPanel { Spacing = 8 };
        destreamStack.Children.Add(new TextBlock
        {
            Text = "DESTREAM", FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
        });
        destreamStack.Children.Add(new TextBlock
        {
            Text = "Donate via Destream",
            FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
        });
        var destreamBtn = new Button
        {
            Content = "Open Destream →", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#a78bfa")),
            Background = new SolidColorBrush(Color.Parse("#1a1a22")),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        destreamBtn.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("https://destream.net/live/zeuthes/donate") { UseShellExecute = true });
        destreamStack.Children.Add(destreamBtn);
        destreamCard.Child = destreamStack;

        mainView.Children.Add(title);
        mainView.Children.Add(subtitle);
        mainView.Children.Add(tradeCard);
        mainView.Children.Add(destreamCard);
        mainView.Children.Add(cryptoCard);
        mainView.Children.Add(closeBtn);

        mainScroll.Content = mainView;
        root.Children.Add(mainScroll);
        win.Content = root;
        win.ShowDialog(GetParentWindow());
    }

    private void ShowCryptoDetail(Window win, Panel root, ScrollViewer mainScroll, StackPanel mainView,
        string sym, string full, string addr, string qrFile, string accent, string warning)
    {
        mainScroll.IsVisible = false;

        var detailScroll = new ScrollViewer();
        var detail = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };

        // Back button
        var backBtn = new Button
        {
            Content = "← Back", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#9d9da5")),
            Background = new SolidColorBrush(Color.Parse("#1f1f24")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        backBtn.Click += (_, _) =>
        {
            root.Children.Remove(detailScroll);
            mainScroll.IsVisible = true;
        };

        // Header
        var header = new StackPanel { Spacing = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.Parse(accent)) { Opacity = 0.15 },
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{full} ({sym})", FontSize = 14, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(accent)),
            },
        };
        header.Children.Add(badge);

        // QR Code
        var qrBorder = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 8),
        };
        try
        {
            using var qrGen = new QRCodeGenerator();
            var qrData = qrGen.CreateQrCode(addr, QRCodeGenerator.ECCLevel.M);
            using var pngQr = new PngByteQRCode(qrData);
            var qrBytes = pngQr.GetGraphic(10, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 });
            using var ms = new System.IO.MemoryStream(qrBytes);
            qrBorder.Child = new Image
            {
                Source = new Bitmap(ms),
                Width = 160, Height = 160, Stretch = Stretch.Uniform,
            };
        }
        catch { qrBorder.Child = new TextBlock { Text = "QR", Width = 160, Height = 160 }; }

        // Warning
        var warnBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2e2410")),
            BorderBrush = new SolidColorBrush(Color.Parse("#fbbf24")) { Opacity = 0.3 },
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
        };
        warnBorder.Child = new TextBlock
        {
            Text = $"⚠ {warning}",
            FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#fbbf24")),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };

        // Address box
        var addrBox = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#111114")),
            BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
        };
        var addrStack = new StackPanel { Spacing = 6 };
        addrStack.Children.Add(new TextBlock
        {
            Text = "ADDRESS", FontSize = 9, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
        });
        addrStack.Children.Add(new TextBlock
        {
            Text = addr, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            TextWrapping = TextWrapping.Wrap,
        });
        addrBox.Child = addrStack;

        // Copy button
        var copyBtn = new Button
        {
            Content = "Copy Address", FontSize = 13, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#ffffff")),
            Background = new SolidColorBrush(Color.Parse(accent)),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(24, 10),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        copyBtn.Click += (_, _) =>
        {
            try { TopLevel.GetTopLevel(win)?.Clipboard?.SetTextAsync(addr); } catch { }
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

    private Window GetParentWindow()
    {
        var el = (StyledElement)this;
        while (el != null)
        {
            if (el is Window w) return w;
            el = el.Parent as StyledElement;
        }
        return null!;
    }

    private struct CandyData
    {
        public Border Element;
        public double StartX, StartY, TargetX, TargetY;
        public double StartRot, EndRot;
        public double Delay, Duration;
        public DateTime StartTime;
    }

    private readonly List<CandyData> _candies = new();
    private DispatcherTimer? _animTimer;
    private DateTime _animStart;
    private bool _animDone;

    private Border CreateCandy(string color, double w, double h)
    {
        var brush = new SolidColorBrush(Color.Parse(color)) { Opacity = 0.92 };
        var dark = new SolidColorBrush(Color.Parse(color)) { Opacity = 0.7 };

        var canvas = new Canvas { Width = w, Height = h };

        // Конфета 1:1 по SVG (200x100): острые хвостики-стрелки, эллипс-тело, блик.
        double sx = w / 200.0, sy = h / 100.0;

        var svgBody = new Border
        {
            Width = 64 * sx, Height = 52 * sy,
            CornerRadius = new CornerRadius(999),
            Background = brush,
        };
        Canvas.SetLeft(svgBody, 68 * sx);
        Canvas.SetTop(svgBody, 24 * sy);
        canvas.Children.Add(svgBody);

        var leftTail = new[] { new Point(20, 20), new Point(75, 47), new Point(75, 53), new Point(20, 80), new Point(35, 50) };
        var rightTail = new[] { new Point(180, 20), new Point(125, 47), new Point(125, 53), new Point(180, 80), new Point(165, 50) };
        foreach (var tail in new[] { leftTail, rightTail })
        {
            var geo = new Avalonia.Media.StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(tail[0].X * sx, tail[0].Y * sy), true);
                for (int k = 1; k < tail.Length; k++)
                    gc.LineTo(new Point(tail[k].X * sx, tail[k].Y * sy));
                gc.EndFigure(true);
            }
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = geo, Fill = dark });
        }

        var glint = new Border
        {
            Width = 14 * sx, Height = 8 * sy,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Colors.White) { Opacity = 0.9 },
            RenderTransform = new RotateTransform(-30),
        };
        Canvas.SetLeft(glint, 87 * sx);
        Canvas.SetTop(glint, 34 * sy);
        canvas.Children.Add(glint);

        var spark = new Border
        {
            Width = 4 * sx, Height = 4 * sy,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Colors.White) { Opacity = 0.9 },
        };
        Canvas.SetLeft(spark, 103 * sx);
        Canvas.SetTop(spark, 32 * sy);
        canvas.Children.Add(spark);



        return new Border
        {
            Child = canvas,
            Width = w, Height = h,
        };
    }

    /// <summary>Случайный сочный цвет конфеты: оттенок разный у каждой, насыщенность и яркость — в пределах палитры.</summary>
    private static string RandomCandyColor(Random rnd)
    {
        double h = rnd.NextDouble() * 6.0;                       // оттенок 0..6
        double s = 0.55 + rnd.NextDouble() * 0.30;
        double v = 0.80 + rnd.NextDouble() * 0.20;
        int i = (int)h; double f = h - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        double r = 0, g = 0, b = 0;
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return "#" + ((byte)(r * 255)).ToString("X2")
                   + ((byte)(g * 255)).ToString("X2")
                   + ((byte)(b * 255)).ToString("X2");
    }

    private void SpawnConfetti()
    {
        var canvas = this.FindControl<Canvas>("ConfettiCanvas");
        if (canvas == null) return;

        var rnd = new Random();
        int count = 22;

        for (int i = 0; i < count; i++)
        {
            var (w, h) = CandyTypes[i % CandyTypes.Length];
            var color = RandomCandyColor(rnd);

            var candy = CreateCandy(color, w, h);

            double startX = -50 + rnd.NextDouble() * 1100;
            double startY = -80 - rnd.NextDouble() * 60;
            double targetX = 40 + rnd.NextDouble() * 1020;
            double targetY = 20 + rnd.NextDouble() * 560;

            Canvas.SetLeft(candy, startX);
            Canvas.SetTop(candy, startY);
            candy.Opacity = 0;
            canvas.Children.Add(candy);

            _candies.Add(new CandyData
            {
                Element = candy,
                StartX = startX, StartY = startY,
                TargetX = targetX, TargetY = targetY,
                StartRot = rnd.NextDouble() * 360 - 180,
                EndRot = rnd.NextDouble() * 90 - 45,
                Delay = i * 0.05,
                Duration = 0.8 + rnd.NextDouble() * 0.4,
                StartTime = DateTime.MaxValue
            });
        }

        _animStart = DateTime.UtcNow;
        _animDone = false;
        _animTimer?.Stop();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (_animDone) return;

        var now = DateTime.UtcNow;
        double elapsed = (now - _animStart).TotalSeconds;
        bool allDone = true;

        for (int i = 0; i < _candies.Count; i++)
        {
            var c = _candies[i];
            if (c.StartTime == DateTime.MaxValue)
            {
                if (elapsed < c.Delay) { allDone = false; continue; }
                c.StartTime = now;
            }

            double t = (now - c.StartTime).TotalSeconds / c.Duration;
            if (t >= 1.0) t = 1.0; else allDone = false;

            // smooth ease out
            double ease = 1.0 - Math.Pow(1.0 - t, 4);

            double x = c.StartX + (c.TargetX - c.StartX) * ease;
            double y = c.StartY + (c.TargetY - c.StartY) * ease;
            double rot = c.StartRot + (c.EndRot - c.StartRot) * ease;

            // fade in quickly, stay visible
            double opacity = t < 0.15 ? t / 0.15 : 1.0;

            Canvas.SetLeft(c.Element, x);
            Canvas.SetTop(c.Element, y);
            c.Element.RenderTransform = new RotateTransform(rot);
            c.Element.Opacity = opacity;

            _candies[i] = c;
        }

        if (allDone) { _animTimer?.Stop(); _animDone = true; }
    }
}
