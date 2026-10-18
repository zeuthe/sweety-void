using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

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
        if (githubBtn != null) githubBtn.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/zeuthe/sweety-void") { UseShellExecute = true });
        if (donateBtn != null) donateBtn.Click += (_, _) => DonateDialog.Show(GetParentWindow());
        if (settingsBtn != null) settingsBtn.Click += (_, _) => _navigateTo("settings");
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

        // Candy 1:1 from the SVG (200x100): sharp arrow tails, ellipse body, glint.
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

    /// <summary>Random juicy candy color: unique hue per candy, saturation and brightness kept inside the palette.</summary>
    private static string RandomCandyColor(Random rnd)
    {
        double h = rnd.NextDouble() * 6.0;                       // hue 0..6
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
