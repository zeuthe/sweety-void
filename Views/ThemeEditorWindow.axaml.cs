using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using VoidUI.Models;
using VoidUI.Services;

namespace VoidUI.Views;



public partial class ThemeEditorWindow : Window
{
    private readonly SettingsService _settings;
    private readonly Action<string> _applyTheme;
    private readonly ThemeSettings _draft;
    private readonly ThemeSettings _original;
    private readonly string _previousTheme;

    private bool _saved;


    private static readonly (string Prop, string Label)[] PaletteRows =
    {
        ("SurfaceBase", "Surface base"),
        ("SurfaceLow", "Surface low"),
        ("SurfaceMid", "Surface mid"),
        ("SurfaceHigh", "Surface high"),
        ("SurfaceOverlay", "Surface overlay"),
        ("BorderSubtle", "Border subtle"),
        ("BorderDefault", "Border default"),
        ("BorderStrong", "Border strong"),
        ("TextPrimary", "Text primary"),
        ("TextSecondary", "Text secondary"),
        ("TextTertiary", "Text tertiary"),
        ("TextDisabled", "Text disabled"),
        ("AccentDefault", "Accent"),
        ("AccentHover", "Accent hover"),
        ("AccentSubtle", "Accent subtle"),
        ("GreenDefault", "Green"),
        ("RedDefault", "Red"),
    };

    private static readonly (string Mode, string Label)[] ButtonModes =
    {
        ("default", "Default"),
        ("glass", "Glass"),
        ("flat", "Flat"),
    };

    private static readonly string[] FontCandidates =
    {
        "Inter", "Segoe UI", "Cascadia Code", "Cascadia Mono", "Consolas",
        "Roboto", "Arial", "Verdana", "Tahoma", "Georgia", "Courier New",
    };

    public ThemeEditorWindow(SettingsService settings, Action<string> applyTheme)
    {
        _settings = settings;
        _applyTheme = applyTheme;
        InitializeComponent();

        var current = _settings.GetSettings();
        _original = current.CustomTheme ?? new ThemeSettings();
        _previousTheme = current.Theme;
        _draft = _original.Clone();

        BuildPalette();
        SetupFonts();
        SetupModes();
        SetupButtons();

        Closed += (_, _) =>
        {

            if (_saved) return;
            _settings.GetSettings().CustomTheme = _original;
            _applyTheme(_previousTheme == "custom" ? "black" : _previousTheme);
        };
    }



    private void BuildPalette()
    {
        var panel = this.FindControl<StackPanel>("PalettePanel");
        if (panel == null) return;

        foreach (var (prop, label) in PaletteRows)
        {
            var value = ReadProp(prop);

            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#35353d")),
                Background = new SolidColorBrush(ParseOr(value, "#00000000")),
                VerticalAlignment = VerticalAlignment.Center
            };

            var box = new TextBox
            {
                Text = value,
                Width = 108,
                FontSize = 11,
                Padding = new Thickness(8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };

            box.TextChanged += (_, _) =>
            {
                if (!Color.TryParse(box.Text?.Trim(), out var c)) return;
                swatch.Background = new SolidColorBrush(c);
                WriteProp(prop, box.Text.Trim());
                Preview();
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("150,30,*"),
                Margin = new Thickness(0, 2)
            };

            var caption = new TextBlock
            {
                Text = label,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse("#9d9da5"))
            };

            Grid.SetColumn(caption, 0);
            Grid.SetColumn(swatch, 1);
            Grid.SetColumn(box, 2);

            row.Children.Add(caption);
            row.Children.Add(swatch);
            row.Children.Add(box);
            panel.Children.Add(row);
        }
    }

    private string ReadProp(string prop) =>
        typeof(ThemeSettings).GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?
            .GetValue(_draft)?.ToString() ?? "";

    private void WriteProp(string prop, string value) =>
        typeof(ThemeSettings).GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?
            .SetValue(_draft, value);


    private void Preview()
    {
        _settings.GetSettings().CustomTheme = _draft;
        _applyTheme("custom");
    }

    private static Color ParseOr(string? hex, string fallback) =>
        Color.TryParse(hex, out var c) ? c : Color.Parse(fallback);



    private void SetupFonts()
    {
        var combo = this.FindControl<ComboBox>("FontCombo");
        var preview = this.FindControl<TextBlock>("FontPreview");
        if (combo == null) return;

        var fonts = FontCandidates.ToList();
        if (!string.IsNullOrWhiteSpace(_draft.FontFamily) && !fonts.Contains(_draft.FontFamily))
            fonts.Insert(0, _draft.FontFamily);

        foreach (var f in fonts) combo.Items?.Add(f);

        var selected = string.IsNullOrWhiteSpace(_draft.FontFamily) ? fonts[0] : _draft.FontFamily;
        combo.SelectedItem = selected;
        preview!.FontFamily = new FontFamily(selected);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string f) return;
            _draft.FontFamily = f;
            preview.FontFamily = new FontFamily(f);
            Preview();
        };
    }



    private void SetupModes()
    {
        var combo = this.FindControl<ComboBox>("ModeCombo");
        if (combo == null) return;

        foreach (var (_, label) in ButtonModes) combo.Items?.Add(label);

        var current = ButtonModes.FirstOrDefault(m => m.Mode == _draft.ButtonMode);
        combo.SelectedItem = string.IsNullOrEmpty(current.Label) ? ButtonModes[0].Label : current.Label;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string label) return;
            var match = ButtonModes.FirstOrDefault(m => m.Label == label);
            if (string.IsNullOrEmpty(match.Mode)) return;
            _draft.ButtonMode = match.Mode;
            Preview();
        };
    }



    private void SetupButtons()
    {
        var cancel = this.FindControl<Button>("CancelBtn");
        if (cancel != null) cancel.Click += (_, _) => Close();

        var save = this.FindControl<Button>("SaveBtn");
        if (save != null) save.Click += (_, _) =>
        {
            _saved = true;
            _settings.GetSettings().CustomTheme = _draft;
            _settings.Save(_settings.GetSettings());
            _applyTheme("custom");
            Close();
        };
    }
}
