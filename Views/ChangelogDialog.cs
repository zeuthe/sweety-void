using System;
using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VoidUI.Services;

namespace VoidUI.Views;

/// <summary>
/// "What's new" window opened from the update bell right after the app has
/// been updated: shows CHANGELOG.txt that ships next to the executable.
/// Self-contained like DonateDialog — the bell only has to ask for it and
/// clear its own notice.
/// </summary>
public static class ChangelogDialog
{
    private static readonly Color PageBg = Color.Parse("#0c0c0e");
    private static readonly Color CardBg = Color.Parse("#18181c");
    private static readonly Color CardBorder = Color.Parse("#232329");
    private static readonly Color RowBg = Color.Parse("#111114");
    private static readonly Color TextPrimary = Color.Parse("#ededef");
    private static readonly Color TextMuted = Color.Parse("#9d9da5");
    private static readonly Color TextDim = Color.Parse("#5c5c66");

    /// <summary>Opens the changelog modally above <paramref name="owner"/>.</summary>
    public static void Show(Window? owner)
    {
        var version = UpdateService.CurrentVersion;

        var win = new Window
        {
            Title = $"What's new — Sweety Void v{version}",
            Width = 640, Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(PageBg),
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
        };
        AutomationProperties.SetAutomationId(win, "ChangelogWindow");

        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(28, 24) };

        stack.Children.Add(Caption($"What's new in v{version}", 20, FontWeight.Bold, TextPrimary));
        stack.Children.Add(Caption("Everything that changed in this release", 12, FontWeight.Normal, TextMuted));

        // The changelog itself — plain text in a monospace face so the
        // markdown-ish structure stays readable.
        stack.Children.Add(new Border
        {
            Background = new SolidColorBrush(CardBg),
            BorderBrush = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16),
            Child = new TextBlock
            {
                Text = LoadChangelog(),
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                Foreground = new SolidColorBrush(TextMuted),
                TextWrapping = TextWrapping.Wrap,
            },
        });

        stack.Children.Add(BuildCloseButton(win));

        var scroll = new ScrollViewer { Content = stack };
        win.Content = scroll;
        win.ShowDialog(owner);
    }

    /// <summary>CHANGELOG.txt next to the exe; a missing/broken file must
    /// still render a window rather than crash the bell flow.</summary>
    private static string LoadChangelog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.txt");
            var text = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            return string.IsNullOrEmpty(text)
                ? "CHANGELOG.txt was not found next to the application."
                : text;
        }
        catch
        {
            return "Could not read CHANGELOG.txt.";
        }
    }

    private static TextBlock Caption(string text, double size, FontWeight weight, Color color) =>
        new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap,
        };

    private static Button BuildCloseButton(Window win)
    {
        var button = new Button
        {
            Content = "Close",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(TextMuted),
            Background = new SolidColorBrush(RowBg),
            BorderBrush = new SolidColorBrush(CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetAutomationId(button, "CloseChangelogBtn");
        button.Click += (_, _) => win.Close();
        return button;
    }
}
