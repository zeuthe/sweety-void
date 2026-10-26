using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VoidUI.Models;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class CrosshairPage : UserControl
{
    private readonly SettingsService _settings;
    private readonly OverlayWindow? _overlayWindow;
    private readonly System.Windows.Threading.Dispatcher? _overlayDispatcher;

    private string _selectedPreset = "dot";
    private string _selectedAnim = "none";
    private bool _spinEnabled;
    private bool _orbitEnabled;
    private double _pickH, _pickS = 1, _pickV = 1;
    private Avalonia.Media.LinearGradientBrush? _svBrush;
    private string _selectedColor = "#22c55e";
    private double _size = 16, _thickness = 3, _opacity = 100, _speed = 5, _rotation = 0, _posX = 0, _posY = 0;
    private double _spinSpeed = 5, _orbitRadius = 30, _orbitSpeed = 5;
    private string _spinDir = "normal", _orbitDir = "normal";
    private int _rainbowSpeed = 5;
    private string _rainbowTheme = "full";
    private string _bindKey = "F6";
    private string _customSymbol = "+";
    private bool _rainbowEnabled;
    private Canvas? _canvas;
    private bool _rainbowCardOpen;
    private DispatcherTimer? _rainbowAnimTimer;
    private double _rainbowExpandPhase;

    private readonly string[] _presetNames = { "dot", "cross", "circle", "bracket", "custom" };
    private readonly string[] _presetLabels = { ".", "+", "o", "[ ]", "S" };
    private readonly string[] _animNames = { "none", "pulse", "scale", "glow" };
    private readonly string[] _colorHexes =
    {
        "#22c55e", "#10b981", "#00ff00", "#00ff88", "#00ffff",
        "#0088ff", "#0044ff", "#0000ff", "#7c5cfc", "#8800ff",
        "#ff00ff", "#ff0088", "#ff0000", "#ff4444", "#ff8800",
        "#ffcc00", "#ffff00", "#88ff00", "#ffffff", "#888888"
    };

    private string[] _rainbowThemeNames = Array.Empty<string>();
    private string[] _rainbowThemeLabels = Array.Empty<string>();
    private string[][] _rainbowThemeColors = Array.Empty<string[]>();

    private DispatcherTimer? _previewTimer;
    private double _animPhase;
    private bool _suppressSliderEvents;







    private Canvas? _previewShapes;
    private Canvas? _previewHalo;
    private CrosshairState? _previewState;
    private TransformGroup? _previewOuterTg;
    private TransformGroup? _previewInnerTg;
    private RotateTransform? _previewOrbit;
    private RotateTransform? _previewSpin;
    private ScaleTransform? _previewScale;
    private readonly List<SolidColorBrush> _previewBrushes = new();
    private byte[] _previewPaletteRgb = Array.Empty<byte>();
    private int _previewPaletteCount;
    private readonly System.Diagnostics.Stopwatch _animClock = new();

    public CrosshairPage(SettingsService settings, OverlayWindow? overlay, System.Windows.Threading.Dispatcher? overlayDispatcher)
    {
        _settings = settings;
        _overlayWindow = overlay;
        _overlayDispatcher = overlayDispatcher;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _canvas = this.FindControl<Canvas>("CrosshairCanvas");



        StartBindWatcher();
        AttachedToVisualTree += (_, _) => StartPreviewAnimation();
        DetachedFromVisualTree += (_, _) =>
        {
            _previewTimer?.Stop(); _previewTimer = null;


            _bindTimer?.Stop(); _bindTimer = null;
        };
        var ch = _settings.GetSettings().Crosshair ?? new CrosshairState();
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = SafeColor(ch.Color);
        _size = ch.Size; _thickness = Math.Max(ch.Thickness, 1); _opacity = ch.Opacity;
        _speed = ch.Speed; _rotation = ch.Rotation; _posX = ch.PosX; _posY = ch.PosY;
        _spinSpeed = ch.SpinSpeed; _spinDir = ch.SpinDir ?? "normal";
        _orbitRadius = ch.OrbitRadius; _orbitSpeed = ch.OrbitSpeed; _orbitDir = ch.OrbitDir ?? "normal";
        _rainbowSpeed = ch.RainbowSpeed; _rainbowTheme = ch.RainbowTheme ?? "full";
        _bindKey = ch.BindKey ?? "F6";
        _customSymbol = ch.Symbol ?? "+";

        SetupButtonGroup("PresetGroup", _presetNames, _presetLabels, _selectedPreset, v => { _selectedPreset = v; ApplyLive(); });
        RebuildAnimGroup();

        var spinBtn = this.FindControl<Button>("SpinBtn");
        if (spinBtn != null)
        {
            UpdateMotionBtn(spinBtn, _spinEnabled);
            spinBtn.Click += (_, _) =>
            {
                _spinEnabled = !_spinEnabled;
                UpdateMotionBtn(spinBtn, _spinEnabled);
                SetVisible("SpinSpeedRow", _spinEnabled);
                SetVisible("SpinDirRow", _spinEnabled);
                ApplyLive();
            };
        }
        var orbitBtn = this.FindControl<Button>("OrbitBtn");
        if (orbitBtn != null)
        {
            UpdateMotionBtn(orbitBtn, _orbitEnabled);
            orbitBtn.Click += (_, _) =>
            {
                _orbitEnabled = !_orbitEnabled;
                UpdateMotionBtn(orbitBtn, _orbitEnabled);
                SetVisible("OrbitRow", _orbitEnabled);
                SetVisible("OrbitSpeedRow", _orbitEnabled);
                SetVisible("OrbitDirRow", _orbitEnabled);
                ApplyLive();
            };
        }

        SetupColorGroup();
        SetupPaletteCard();
        SetupRainbowCard();

        SetupSlider("SizeSlider", "SizeValue", _size, v => { _size = v; ApplyLive(); }, "px");
        SetupSlider("ThicknessSlider", "ThicknessValue", _thickness, v => { _thickness = v; ApplyLive(); }, "px");
        SetupSlider("OpacitySlider", "OpacityValue", _opacity, v => { _opacity = v; ApplyLive(); }, "%");
        SetupSlider("SpeedSlider", "SpeedValue", _speed, v => { _speed = v; ApplyLive(); }, "x");
        SetupSlider("RotationSlider", "RotationValue", _rotation, v => { _rotation = v; ApplyLive(); }, "");
        SetupSlider("PosXSlider", "PosXValue", _posX, v => { _posX = v; UpdatePosInputs(); ApplyLive(); }, "");
        SetupSlider("PosYSlider", "PosYValue", _posY, v => { _posY = v; UpdatePosInputs(); ApplyLive(); }, "");
        SetupSlider("SpinSpeedSlider", "SpinSpeedValue", _spinSpeed, v => { _spinSpeed = v; ApplyLive(); }, "x");
        SetupSlider("OrbitRadiusSlider", "OrbitRadiusValue", _orbitRadius, v => { _orbitRadius = v; ApplyLive(); }, "");
        SetupSlider("OrbitSpeedSlider", "OrbitSpeedValue", _orbitSpeed, v => { _orbitSpeed = v; ApplyLive(); }, "x");

        SetupToggleButton("SpinDirBtn", new[] { "Normal", "Reverse" }, _spinDir == "reverse" ? 1 : 0, idx =>
        {
            _spinDir = idx == 0 ? "normal" : "reverse";
            ApplyLive();
        });
        SetupToggleButton("OrbitDirBtn", new[] { "Normal", "Reverse" }, _orbitDir == "reverse" ? 1 : 0, idx =>
        {
            _orbitDir = idx == 0 ? "normal" : "reverse";
            ApplyLive();
        });

        SetupButton("StartOverlayBtn", () => _overlayDispatcher?.Invoke(() => _overlayWindow?.Show()));
        SetupButton("StopOverlayBtn", () => _overlayDispatcher?.Invoke(() => _overlayWindow?.Hide()));
        SetupButton("ResetBtn", () => { ResetDefaults(); SyncAllControls(); ApplyLive(); SaveSettings(); ShowConfigStatus("Reset to defaults"); });
        SetupButton("PreviewEyeBtn", TogglePreview);
        SetupButton("CenterAllBtn", () => { _posX = 0; _posY = 0; SyncSlider("PosXSlider", 0); SyncSlider("PosYSlider", 0); UpdatePosInputs(); SaveSettings(); ApplyLive(); });
        SetupButton("SaveCenterBtn", () => { _posX = 0; _posY = 0; SyncSlider("PosXSlider", 0); SyncSlider("PosYSlider", 0); UpdatePosInputs(); SaveSettings(); ApplyLive(); });

        SetupButton("AddAnimBtn", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Rainbow Preset",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            var srcPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(srcPath)) return;
            try
            {
                // Validate before copying: a garbage JSON used to be copied
                // silently and then ignored by RebuildRainbowThemes forever.
                AnimPreset? probe = null;
                try
                {
                    probe = Newtonsoft.Json.JsonConvert.DeserializeObject<AnimPreset>(File.ReadAllText(srcPath));
                }
                catch { probe = null; }

                if (probe == null || string.IsNullOrWhiteSpace(probe.Name) || probe.Palette == null || probe.Palette.Count == 0)
                {
                    ShowConfigMsg("Invalid preset: need name + palette");
                    return;
                }

                var presetsDir = UserPresets.AnimPresetsDir;
                Directory.CreateDirectory(presetsDir);
                var dstPath = System.IO.Path.Combine(presetsDir, System.IO.Path.GetFileName(srcPath));
                File.Copy(srcPath, dstPath, true);
                RebuildRainbowThemes();
                ShowConfigMsg($"Imported: {probe.Name}");
            }
            catch (Exception ex) { ShowConfigMsg("Import failed: " + ex.Message); }
        });

        var bindInput = this.FindControl<Button>("BindInput");
        if (bindInput != null)
        {
            bindInput.Content = _bindKey;
            bindInput.Click += async (_, _) => await ShowBindDialogAsync();
        }
        SetupButton("BindSetBtn", async () => await ShowBindDialogAsync());

        var applyColorBtn = this.FindControl<Button>("ApplyColorBtn");
        if (applyColorBtn != null)
        {
            applyColorBtn.Click += (_, _) =>
            {
                var tb = this.FindControl<TextBox>("CustomColorInput");
                if (tb != null && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    var hex = tb.Text.Trim();
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    try { Color.Parse(hex); _selectedColor = hex; SyncColorButtons(); ApplyLive(); } catch { }
                }
            };
        }

        var symbolInput = this.FindControl<TextBox>("SymbolInput");
        if (symbolInput != null)
        {
            symbolInput.Text = _customSymbol;
            symbolInput.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrEmpty(symbolInput.Text)) { _customSymbol = symbolInput.Text; if (_selectedPreset == "custom") ApplyLive(); }
            };
        }

        SetupButton("CopyConfigBtn2", async () =>
        {
            var ch = BuildCurrentState();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) await topLevel.Clipboard.SetTextAsync(encoded);
            ShowConfigMsg("Copied! Share this code with a friend");
        });

        SetupButton("PasteConfigBtn", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var text = await topLevel.Clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { ShowConfigMsg("Clipboard is empty"); return; }
            var t = text.Trim().Trim('"');
            CrosshairState? ch = null;
            // 1) share code (base64)  2) plain JSON (copied from a file / Save->Copy)
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t));
                ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(json);
            }
            catch { ch = null; }
            if (ch == null)
            {
                try { ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(t); }
                catch { ch = null; }
            }
            if (ch == null) { ShowConfigMsg("Invalid config"); return; }

            var incoming = NormalizeState(ch);
            var same = SameState(incoming, BuildCurrentState());
            try
            {
                if (!same && topLevel is Window owner)
                {
                    ShowConfigMsg("Confirming paste...");
                    var ok = await ConfirmPasteAsync(owner);
                    if (!ok) { ShowConfigMsg("Paste cancelled"); return; }
                }
                ApplyCrosshairState(incoming);
                ShowConfigMsg(same ? "Applied (identical)" : "Applied!");
            }
            catch (Exception ex) { ShowConfigMsg("Paste error: " + ex.Message); }
        });

        SetupConfigs();

        ApplyLive();

        // Global search can hand us a config/preset before Loaded runs —
        // OnLoaded overwrites state from saved settings, which used to revert
        // the just-loaded config. Apply the queued action only now.
        if (_pendingConfigPath != null)
        {
            var p = _pendingConfigPath;
            _pendingConfigPath = null;
            LoadConfig(p);
        }
        else if (_pendingPreset != null)
        {
            var p = _pendingPreset;
            _pendingPreset = null;
            SelectPreset(p);
        }
    }

    private string? _pendingConfigPath;
    private string? _pendingPreset;

    private void SetupConfigs()
    {
        SetupButton("SaveConfigBtn", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            // Show name input dialog
            var win = new Window
            {
                Width = 340,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.Transparent,
                SystemDecorations = SystemDecorations.None,
                CanResize = false,
                ShowInTaskbar = false,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
            };

            var card = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#16161c")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20),
                BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
            };

            var title = new TextBlock
            {
                Text = "Название конфига",
                Foreground = new SolidColorBrush(Color.Parse("#ededef")),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var nameBox = new TextBox
            {
                Watermark = "например: my-crosshair",
                FontSize = 14,
                MinWidth = 200,
                MaxWidth = 280,
                Height = 36,
                Background = new SolidColorBrush(Color.Parse("#111114")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 0),
                Text = $"config_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };

            var saveBtn = new Button
            {
                Content = "Сохранить",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            var cancelBtn = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#1e1e26")),
                Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
                CornerRadius = new CornerRadius(9),
                BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            // BUGFIX: buttons were created but never added to btnRow, so the
            // save dialog rendered an empty button row (only Enter/Esc worked).
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(saveBtn);

            var errText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse("#ef4444")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false,
                Margin = new Thickness(0, 6, 0, 0),
            };

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(title);
            panel.Children.Add(nameBox);
            panel.Children.Add(errText);
            panel.Children.Add(btnRow);
            card.Child = panel;
            win.Content = card;

            void ShowErr(string msg)
            {
                errText.Text = msg;
                errText.IsVisible = true;
            }

            bool ValidateName(string rawName)
            {
                var name = (rawName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    ShowErr("Введите имя конфига");
                    return false;
                }
                var first = name[0];
                if (!char.IsLetterOrDigit(first) && first != '_')
                {
                    ShowErr("Имя должно начинаться с буквы, цифры или _");
                    return false;
                }
                return true;
            }

            void SaveAndClose(string rawName)
            {
                if (!ValidateName(rawName)) return;

                var name = rawName.Trim();
                var configsDir = UserPresets.ConfigsDir;
                Directory.CreateDirectory(configsDir);

                var safeName = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "config";

                // Ensure unique name
                var path = System.IO.Path.Combine(configsDir, $"{safeName}.json");
                if (File.Exists(path))
                {
                    var baseName = safeName;
                    int i = 1;
                    while (File.Exists(path))
                    {
                        path = System.IO.Path.Combine(configsDir, $"{baseName}_{i}.json");
                        i++;
                    }
                }

                try
                {
                    var ch = BuildCurrentState();
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(path, json);
                    UserPresets.Touch(); // invalidate global search index
                }
                catch (Exception ex)
                {
                    ShowErr("Ошибка сохранения: " + ex.Message);
                    return;
                }

                ShowConfigStatus($"Сохранено: {System.IO.Path.GetFileNameWithoutExtension(path)}");
                RefreshConfigsList();
                win.Close(true);
            }

            // Exactly one handler per button (previous version registered
            // save/cancel twice, causing double-close and invisible errors).
            saveBtn.Click += (_, _) => SaveAndClose(nameBox.Text);
            cancelBtn.Click += (_, _) => win.Close(null);

            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) win.Close(null);
                else if (e.Key == Key.Enter) SaveAndClose(nameBox.Text);
            };

            if (TopLevel.GetTopLevel(this) is Window owner)
                await win.ShowDialog(owner);
            else
                win.Show();
        });

        SetupButton("CopyConfigBtn", () =>
        {
            var ch = BuildCurrentState();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch, Newtonsoft.Json.Formatting.Indented);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                topLevel.Clipboard.SetTextAsync(json);
            ShowConfigStatus("Copied to clipboard");
        });

        SetupButton("RefreshConfigsBtn", () => RefreshConfigsList());
        RefreshConfigsList();
    }

    private void RefreshConfigsList()
    {
        var panel = this.FindControl<WrapPanel>("ConfigsGroup");
        if (panel == null) return;
        panel.Children.Clear();

        var configsDir = UserPresets.ConfigsDir;
        if (!Directory.Exists(configsDir)) return;

        foreach (var file in Directory.GetFiles(configsDir, "*.json"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var btn = new Button
            {
                Content = name, MinWidth = 60, Height = 24, Tag = file, FontSize = 10, Classes = { "pill" },
                BorderBrush = new SolidColorBrush(Color.Parse("#232329"))
            };
            btn.Click += (_, _) => LoadConfig(file);
            // Assign through the ContextMenu property: Avalonia's built-in
            // ContextRequested handling then opens it at the pointer position
            // (exactly like Windows). Calling menu.Open() manually threw
            // ArgumentNullException because the menu was never attached.
            btn.ContextMenu = BuildConfigContextMenu(file, name);
            panel.Children.Add(btn);
        }
    }

    public void LoadConfigFile(string path)
    {
        // Before Loaded the page state gets rebuilt from saved settings,
        // so defer instead of letting OnLoaded overwrite the loaded config.
        if (!IsLoaded) { _pendingConfigPath = path; _pendingPreset = null; return; }
        LoadConfig(path);
    }

    /// <summary>Windows-style context menu for saved configs: Выбрать /
    /// Переименовать / Открыть в блокноте / Удалить (red).</summary>
    private ContextMenu BuildConfigContextMenu(string file, string name)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 6),
            MinWidth = 190,
            FontSize = 13,
        };

        // Style all MenuItems inside this context menu to match app theme
        var itemNormal = new Style(x => x.OfType<MenuItem>());
        itemNormal.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
        itemNormal.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Color.Parse("#ededef"))));
        itemNormal.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(12, 8)));
        itemNormal.Setters.Add(new Setter(MenuItem.CornerRadiusProperty, new CornerRadius(6)));
        itemNormal.Setters.Add(new Setter(MenuItem.MinHeightProperty, 32.0));
        menu.Styles.Add(itemNormal);

        var itemHover = new Style(x => x.OfType<MenuItem>().Class(":pointerover"));
        itemHover.Setters.Add(new Setter(MenuItem.BackgroundProperty, new SolidColorBrush(Color.Parse("#27272e"))));
        menu.Styles.Add(itemHover);

        // Separator style
        var sepStyle = new Style(x => x.OfType<Separator>());
        sepStyle.Setters.Add(new Setter(Separator.BackgroundProperty, new SolidColorBrush(Color.Parse("#2a2a34"))));
        sepStyle.Setters.Add(new Setter(Separator.MarginProperty, new Thickness(8, 4)));
        sepStyle.Setters.Add(new Setter(Separator.HeightProperty, 1.0));
        menu.Styles.Add(sepStyle);

        // ── 1. Выбрать ──
        var selectItem = new MenuItem { Header = "Выбрать" };
        selectItem.Click += (_, _) => LoadConfig(file);
        menu.Items.Add(selectItem);

        // ── 2. Переименовать ──
        var renameItem = new MenuItem { Header = "Переименовать" };
        renameItem.Click += (_, _) => ShowRenameDialog(file, name);
        menu.Items.Add(renameItem);

        // ── 3. Открыть в блокноте ──
        var notepadItem = new MenuItem { Header = "Открыть в блокноте" };
        notepadItem.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{file}\"",
                    UseShellExecute = true
                });
            }
            catch { ShowConfigStatus("Не удалось открыть в блокноте"); }
        };
        menu.Items.Add(notepadItem);

        // ── Separator before destructive action ──
        menu.Items.Add(new Separator());

        // ── 4. Удалить (red) ──
        var deleteItem = new MenuItem { Header = "Удалить" };
        deleteItem.Foreground = new SolidColorBrush(Color.Parse("#ef4444"));
        deleteItem.Click += (_, _) =>
        {
            try
            {
                File.Delete(file);
                RefreshConfigsList();
                UserPresets.Touch();
                ShowConfigStatus($"Удалено: {name}");
            }
            catch { ShowConfigStatus("Не удалось удалить"); }
        };
        menu.Items.Add(deleteItem);

        // Returned to the caller which assigns it to btn.ContextMenu; opening
        // happens automatically in ContextRequested, positioned at the pointer
        // (ContextMenu's default Placement is PlacementMode.Pointer).
        return menu;
    }

    /// <summary>App-styled dialog to rename a saved config.</summary>
    private async void ShowRenameDialog(string file, string oldName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner) return;

        var win = new Window
        {
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000"),
        };

        var title = new TextBlock
        {
            Text = "Переименовать конфиг",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var nameBox = new TextBox
        {
            Text = oldName,
            FontSize = 14,
            MinWidth = 240,
            Height = 36,
            Background = new SolidColorBrush(Color.Parse("#111114")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 0),
        };

        var okBtn = new Button
        {
            Content = "Применить",
            Width = 110,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var cancelBtn = new Button
        {
            Content = "Отмена",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#1e1e26")),
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var errText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#ef4444")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 6, 0, 0),
        };

        okBtn.Click += (_, _) =>
        {
            var newName = (nameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                errText.Text = "Введите имя конфига";
                errText.IsVisible = true;
                return;
            }

            newName = string.Join("_", newName.Split(System.IO.Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(newName))
            {
                errText.Text = "Недопустимое имя";
                errText.IsVisible = true;
                return;
            }

            var dir = UserPresets.ConfigsDir;
            var newPath = System.IO.Path.Combine(dir, newName + ".json");

            // Renaming to the same file (or only changing letter case) is a no-op.
            if (string.Equals(newPath, file, StringComparison.OrdinalIgnoreCase))
            {
                win.Close(true);
                return;
            }

            if (File.Exists(newPath))
            {
                // Error must be visible inside the modal dialog, not behind it.
                errText.Text = "Такое имя уже существует";
                errText.IsVisible = true;
                return;
            }

            try
            {
                File.Move(file, newPath, overwrite: false);
                RefreshConfigsList();
                UserPresets.Touch();
                ShowConfigStatus($"Переименовано: {newName}");
                win.Close(true);
            }
            catch (Exception ex)
            {
                errText.Text = "Ошибка переименования: " + ex.Message;
                errText.IsVisible = true;
            }
        };

        cancelBtn.Click += (_, _) => win.Close(false);

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close(false);
            else if (e.Key == Key.Enter) okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(title);
        panel.Children.Add(nameBox);
        panel.Children.Add(errText);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;

        await win.ShowDialog<bool>(owner);
    }

    public void SelectPreset(string presetName)
    {
        if (!_presetNames.Contains(presetName)) return;
        if (!IsLoaded) { _pendingPreset = presetName; _pendingConfigPath = null; return; }
        _selectedPreset = presetName;
        SyncButtonGroupActive("PresetGroup", _presetNames, _selectedPreset);
        ApplyLive();
    }

    private void LoadConfig(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(json);
            if (ch == null) return;
            ApplyCrosshairState(ch);
            ShowConfigStatus($"Loaded: {System.IO.Path.GetFileNameWithoutExtension(path)}");
        }
        catch { ShowConfigStatus("Failed to load"); }
    }

    private async Task<bool> ConfirmPasteAsync(Window owner)
    {
        var win = new Window
        {
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
        };

        var title = new TextBlock
        {
            Text = "Replace current settings?",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        var body = new TextBlock
        {
            Text = "The pasted crosshair differs from the active one. Apply it and save?",
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var replaceBtn = new Button
        {
            Content = "Replace",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand)
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
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(replaceBtn);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;

        replaceBtn.Click += (_, _) => win.Close(true);
        cancelBtn.Click += (_, _) => win.Close(false);
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close(false);
            else if (e.Key == Key.Enter) win.Close(true);
        };

        return await win.ShowDialog<bool>(owner);
    }

    /// <summary>


    /// </summary>
    private async Task ShowBindDialogAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner) return;

        var win = new Window
        {
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
        };

        var title = new TextBlock
        {
            Text = "Overlay bind",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        var hint = new TextBlock
        {
            Text = "Press any key or combination (e.g. Ctrl + Shift + F6)",
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };


        var display = new TextBlock
        {
            Text = _bindKey,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Foreground = new SolidColorBrush(Color.Parse("#7c5cfc")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var displayBox = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#111114")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Height = 62,
            Margin = new Thickness(0, 12, 0, 0),
            Child = display,
            Focusable = true
        };


        var manualBox = new TextBox
        {
            Text = _bindKey,
            Watermark = "or type here: F6, Ctrl+F7...",
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var saveBtn = new Button
        {
            Content = "Save",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand)
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
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(saveBtn);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(title);
        panel.Children.Add(hint);
        panel.Children.Add(displayBox);
        panel.Children.Add(manualBox);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;



        displayBox.KeyDown += (_, e) =>
        {
            var k = e.Key;
            if (IsBindModifierKey(k))
            {
                display.Text = BuildBindModifierHint(e.KeyModifiers) + "...";
                e.Handled = true;
                return;
            }
            if (k == Key.Escape) { e.Handled = true; win.Close(null); return; }
            var combo = BuildBindCombo(k, e.KeyModifiers);
            display.Text = combo;
            manualBox.Text = combo;
            e.Handled = true;
        };


        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close(null);
            else if (e.Key == Key.Enter) saveBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        saveBtn.Click += (_, _) =>
        {
            var result = NormalizeBind(manualBox.Text);
            win.Close(string.IsNullOrEmpty(result) ? _bindKey : result);
        };
        cancelBtn.Click += (_, _) => win.Close(null);
        win.Opened += (_, _) => { displayBox.Focus(); };

        var result2 = await win.ShowDialog<string>(owner);
        if (!string.IsNullOrWhiteSpace(result2) && result2 != _bindKey)
        {
            _bindKey = result2;
            var bindBtn = this.FindControl<Button>("BindInput");
            if (bindBtn != null) bindBtn.Content = _bindKey;
            ApplyLive();
        }
    }

    private static bool IsBindModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private static string BuildBindModifierHint(KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        return parts.Count > 0 ? string.Join("+", parts) + "+" : "";
    }

    private static string BuildBindCombo(Key key, KeyModifiers mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(BindKeyName(key));
        return string.Join("+", parts);
    }

    private static string BindKeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return key.ToString().Substring(1);
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + key.ToString().Replace("NumPad", "");
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Enter => "Enter",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.CapsLock => "CapsLock",
            Key.PrintScreen => "PrtSc",
            Key.Scroll => "ScrollLock",
            Key.Pause => "Pause",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.Oem1 => ";",
            Key.Oem7 => "'",
            Key.Oem4 => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem3 => "`",
            _ => key.ToString()
        };
    }


    private static string NormalizeBind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var parts = raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = new List<string>();
        string? main = null;
        foreach (var p in parts)
        {
            switch (p.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": mods.Add("Ctrl"); break;
                case "SHIFT": mods.Add("Shift"); break;
                case "ALT": mods.Add("Alt"); break;
                case "WIN": case "WINDOWS": case "META": mods.Add("Win"); break;
                default:
                    if (main == null)
                        main = p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..];
                    break;
            }
        }
        if (main == null) return "";
        mods.Add(main);
        return string.Join("+", mods);
    }

    private CrosshairState BuildCurrentState() => new CrosshairState
    {
        Preset = _selectedPreset, Animation = _selectedAnim,
        SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
        Color = _selectedColor, Size = (int)_size, Thickness = _thickness, Opacity = (int)_opacity,
        Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
        SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
        OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
        RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
        BindKey = _bindKey, Symbol = _customSymbol
    };

    // Fills the same defaults ApplyCrosshairState uses, so a partial config
    // deserializes to the exact state it would end up in after applying.
    /// <summary>Validates a color string coming from settings, an imported
    /// preset or a pasted share code; falls back instead of letting
    /// Color.Parse throw and crash the preview.</summary>
    private static string SafeColor(string? hex, string fallback = "#22c55e")
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        return Color.TryParse(hex, out _) ? hex! : fallback;
    }

    private static CrosshairState NormalizeState(CrosshairState ch) => new CrosshairState
    {
        Preset = ch.Preset ?? "dot", Animation = ch.Animation ?? "none",
        SpinEnabled = ch.SpinEnabled, OrbitEnabled = ch.OrbitEnabled,
        Color = SafeColor(ch.Color), Size = ch.Size, Thickness = Math.Max(ch.Thickness, 1),
        Opacity = ch.Opacity, Speed = ch.Speed, Rotation = ch.Rotation,
        PosX = ch.PosX, PosY = ch.PosY,
        SpinSpeed = ch.SpinSpeed, SpinDir = ch.SpinDir ?? "normal",
        OrbitRadius = ch.OrbitRadius, OrbitSpeed = ch.OrbitSpeed, OrbitDir = ch.OrbitDir ?? "normal",
        RainbowEnabled = ch.RainbowEnabled, RainbowSpeed = ch.RainbowSpeed,
        RainbowTheme = ch.RainbowTheme ?? "full",
        BindKey = ch.BindKey ?? "F6", Symbol = ch.Symbol ?? "+"
    };

    private static bool SameState(CrosshairState a, CrosshairState b) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(a) == Newtonsoft.Json.JsonConvert.SerializeObject(b);

    private void ApplyCrosshairState(CrosshairState ch)
    {
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = SafeColor(ch.Color);
        _size = ch.Size; _thickness = Math.Max(ch.Thickness, 1); _opacity = ch.Opacity;
        _speed = ch.Speed; _rotation = ch.Rotation;
        _posX = ch.PosX; _posY = ch.PosY;
        _spinSpeed = ch.SpinSpeed; _spinDir = ch.SpinDir ?? "normal";
        _orbitRadius = ch.OrbitRadius; _orbitSpeed = ch.OrbitSpeed; _orbitDir = ch.OrbitDir ?? "normal";
        _rainbowEnabled = ch.RainbowEnabled;
        _rainbowSpeed = ch.RainbowSpeed; _rainbowTheme = ch.RainbowTheme ?? "full";
        _bindKey = ch.BindKey ?? "F6";
        _customSymbol = ch.Symbol ?? "+";
        SyncAllControls();
        ApplyLive();
    }

    private DispatcherTimer? _configMsgTimer;

    private void ShowConfigMsg(string msg)
    {
        var tb = this.FindControl<TextBlock>("ConfigMsg");
        if (tb == null) return;
        tb.Text = msg;
        tb.IsVisible = true;

        // Single reused timer (a new timer per call let stale ones hide the
        // fresh message early).
        if (_configMsgTimer == null)
        {
            _configMsgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _configMsgTimer.Tick += (_, _) =>
            {
                tb.IsVisible = false;
                _configMsgTimer!.Stop();
            };
        }
        _configMsgTimer.Stop();
        _configMsgTimer.Start();
    }

    private DispatcherTimer? _configStatusTimer;

    private void ShowConfigStatus(string msg)
    {
        var tb = this.FindControl<TextBlock>("ConfigStatus");
        if (tb == null) return;
        tb.Text = msg;
        tb.IsVisible = true;

        // Reuse a single timer: creating a new one per call let stale timers
        // hide the freshly shown message early.
        if (_configStatusTimer == null)
        {
            _configStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _configStatusTimer.Tick += (_, _) =>
            {
                tb.IsVisible = false;
                _configStatusTimer!.Stop();
            };
        }
        _configStatusTimer.Stop();
        _configStatusTimer.Start();
    }

    private void SetupButton(string name, Action onClick)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += (_, _) => onClick();
    }

    private void SetupToggleButton(string name, string[] labels, int initial, Action<int> onToggle)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        int idx = initial;
        btn.Content = labels[idx];
        btn.Click += (_, _) => { idx = (idx + 1) % labels.Length; btn.Content = labels[idx]; onToggle(idx); };
    }

    private void UpdateMotionBtn(Button btn, bool active)
    {
        if (active) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
        else { btn.Classes.Remove("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
    }

    private void SetVisible(string name, bool visible)
    {
        var el = this.FindControl<StackPanel>(name);
        if (el != null) el.IsVisible = visible;
    }

    private void SetupColorGroup()    {
        var panel = this.FindControl<WrapPanel>("ColorGroup");
        if (panel == null) return;
        foreach (var hex in _colorHexes)
            panel.Children.Add(MakeColorSwatch(hex));
    }


    private bool _previewVisible = true;

    private Avalonia.Threading.DispatcherTimer? _bindTimer;
    private bool _bindWasDown;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void StartBindWatcher()
    {
        if (_bindTimer != null) return;
        _bindTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _bindTimer.Tick += (_, _) => PollBind();
        _bindTimer.Start();
    }

    private void PollBind()
    {

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is Avalonia.Controls.TextBox) { _bindWasDown = false; return; }

        var parts = (_bindKey ?? "F6").Split('+');
        var main = parts.Length == 0 ? "F6" : parts[^1].Trim();
        var vk = VkFor(main);
        if (vk == null) { _bindWasDown = false; return; }

        bool down = GetAsyncKeyState(vk.Value) < 0;
        if (parts.Any(p => p.Trim() is "Ctrl" or "Control")) down = down && GetAsyncKeyState(0x11) < 0;
        if (parts.Any(p => p.Trim() == "Shift")) down = down && GetAsyncKeyState(0x10) < 0;
        if (parts.Any(p => p.Trim() == "Alt")) down = down && GetAsyncKeyState(0x12) < 0;

        if (down && !_bindWasDown) ToggleOverlayByBind();
        _bindWasDown = down;
    }


    private void ToggleOverlayByBind()
    {
        if (_overlayWindow == null || _overlayDispatcher == null) return;
        _overlayDispatcher.Invoke(() =>
        {
            if (_overlayWindow.IsVisible) _overlayWindow.Hide();
            else _overlayWindow.Show();
        });
    }


    private static int? VkFor(string name) => name switch
    {
        "Space" => 0x20, "Tab" => 0x09, "Enter" => 0x0D, "Backspace" => 0x08,
        "Delete" => 0x2E, "Insert" => 0x2D, "Home" => 0x24, "End" => 0x23,
        "PageUp" => 0x21, "PageDown" => 0x22, "Up" => 0x26, "Down" => 0x28,
        "Left" => 0x25, "Right" => 0x27, "CapsLock" => 0x14, "PrtSc" => 0x2C,
        "ScrollLock" => 0x91, "Pause" => 0x13,
        _ when name.Length == 1 && char.IsLetter(name[0]) => char.ToUpperInvariant(name[0]),
        _ when name.Length == 1 && char.IsDigit(name[0]) => name[0],
        _ when name.StartsWith("Num") => (int?)null,
        _ when name.StartsWith("F") && int.TryParse(name.Substring(1), out var f) && f >= 1 && f <= 24 => 0x70 + f - 1,
        _ => null
    };

    private void TogglePreview()
    {
        var box = this.FindControl<Border>("PreviewBox");
        var icon = this.FindControl<Avalonia.Controls.Shapes.Path>("PreviewEyeIcon");
        if (box == null) return;

        _previewVisible = !_previewVisible;
        box.IsVisible = _previewVisible;

        if (icon != null) icon.Opacity = _previewVisible ? 1.0 : 0.35;
    }


    private void SetupPaletteCard()
    {
        SetupPicker();

        Dispatcher.UIThread.Post(UpdatePickerVisuals, DispatcherPriority.Loaded);
    }

    private void SetupPicker()
    {
        var svArea = this.FindControl<Border>("PickerSvArea");
        var hueBar = this.FindControl<Border>("PickerHueBar");
        if (svArea == null || hueBar == null) return;

        _svBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
        };
        _svBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
        _svBrush.GradientStops.Add(new GradientStop(Colors.Red, 1));
        svArea.Background = _svBrush;


        var overlay = this.FindControl<Border>("PickerSvOverlay");
        if (overlay != null)
        {
            var ovBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative)
            };
            ovBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            ovBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
            overlay.Background = ovBrush;
        }


        var hueStops = new (Color c, double o)[]
        {
            (Color.FromRgb(255, 0, 0), 0),
            (Color.FromRgb(255, 255, 0), 1.0/6),
            (Color.FromRgb(0, 255, 0), 2.0/6),
            (Color.FromRgb(0, 255, 255), 3.0/6),
            (Color.FromRgb(0, 0, 255), 4.0/6),
            (Color.FromRgb(255, 0, 255), 5.0/6),
            (Color.FromRgb(255, 0, 0), 1),
        };
        var hueBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
        };
        foreach (var (c, o) in hueStops)
            hueBrush.GradientStops.Add(new GradientStop(c, o));
        hueBar.Background = hueBrush;

        HexToHsv(_selectedColor, out _pickH, out _pickS, out _pickV);

        void ApplyPick()
        {
            _selectedColor = HsvToHex(_pickH, _pickS, _pickV);
            SyncColorButtons();
            ApplyLive();
        }

        void SvUpdate(Point p)
        {
            var b = svArea.Bounds;
            _pickS = Math.Clamp(p.X / Math.Max(1, b.Width), 0, 1);
            _pickV = Math.Clamp(1 - p.Y / Math.Max(1, b.Height), 0, 1);
            ApplyPick();
        }

        svArea.PointerPressed += (_, e) =>
        {
            e.Pointer.Capture(svArea);
            SvUpdate(e.GetPosition(svArea));
            e.Handled = true;
        };
        svArea.PointerMoved += (_, e) => { if (e.Pointer.Captured == svArea) SvUpdate(e.GetPosition(svArea)); };
        svArea.PointerReleased += (_, e) => e.Pointer.Capture(null);

        void HueUpdate(Point p)
        {
            var b = hueBar.Bounds;
            _pickH = Math.Clamp(p.X / Math.Max(1, b.Width), 0, 1) * 360;
            ApplyPick();
        }

        hueBar.PointerPressed += (_, e) =>
        {
            e.Pointer.Capture(hueBar);
            HueUpdate(e.GetPosition(hueBar));
            e.Handled = true;
        };
        hueBar.PointerMoved += (_, e) => { if (e.Pointer.Captured == hueBar) HueUpdate(e.GetPosition(hueBar)); };
        hueBar.PointerReleased += (_, e) => e.Pointer.Capture(null);

        UpdatePickerVisuals();
    }

    private void UpdatePickerVisuals()
    {
        var svArea = this.FindControl<Border>("PickerSvArea");
        var dot = this.FindControl<Border>("PickerDot");
        var tb = this.FindControl<TextBox>("CustomColorInput");
        if (svArea == null) return;

        HexToHsv(_selectedColor, out _pickH, out _pickS, out _pickV);

        var hueColor = HsvToColor(_pickH, 1, 1);
        if (_svBrush != null && _svBrush.GradientStops.Count > 1 && _svBrush.GradientStops[1].Color != hueColor)
            _svBrush.GradientStops[1].Color = hueColor;

        var b = svArea.Bounds;
        if (dot != null && b.Width > 0)
        {
            double x = Math.Clamp(_pickS * b.Width - 7, -7, b.Width - 7);
            double y = Math.Clamp((1 - _pickV) * b.Height - 7, -7, b.Height - 7);
            if (Canvas.GetLeft(dot) != x) Canvas.SetLeft(dot, x);
            if (Canvas.GetTop(dot) != y) Canvas.SetTop(dot, y);
            var dotColor = Color.Parse(_selectedColor);
            if (dot.Background is not SolidColorBrush br || br.Color != dotColor)
                dot.Background = new SolidColorBrush(dotColor);
        }

        var hex = _selectedColor.ToLowerInvariant();
        if (tb != null && !tb.IsFocused && tb.Text != hex) tb.Text = hex;
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static string HsvToHex(double h, double s, double v)
    {
        var c = HsvToColor(h, s, v);
        return $"#{c.R:x2}{c.G:x2}{c.B:x2}";
    }

    private static void HexToHsv(string hex, out double h, out double s, out double v)
    {
        h = 0; s = 1; v = 1;
        try
        {
            var c = Color.Parse(hex);
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d > 0)
            {
                if (max == r) h = 60 * ((g - b) / d % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
                if (h < 0) h += 360;
            }
        }
        catch { }
    }

    private Button MakeColorSwatch(string hex)
    {
        var btn = new Button
        {
            Width = 24, Height = 24, MinWidth = 24, MinHeight = 24,
            MaxWidth = 24, MaxHeight = 24, Padding = new Thickness(0),
            Tag = hex, Classes = { "pill" },
            BorderBrush = new SolidColorBrush(Color.Parse(hex == _selectedColor ? "#ffffff" : "#232329")),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.Parse(hex))
        };
        AutomationProperties.SetName(btn, hex);
        btn.Click += (_, _) =>
        {
            _selectedColor = hex;
            SyncColorButtons();
            var tb = this.FindControl<TextBox>("CustomColorInput");
            if (tb != null) tb.Text = hex;
            ApplyLive();
        };
        return btn;
    }

    private void SyncColorButtons()
    {
        var panel = this.FindControl<WrapPanel>("ColorGroup");
        if (panel != null)
            foreach (var child in panel.Children)
                if (child is Button b && b.Tag is string hex)
                    b.BorderBrush = new SolidColorBrush(Color.Parse(hex == _selectedColor ? "#ffffff" : "#232329"));
        UpdatePickerVisuals();
    }

    private void SetupRainbowCard()
    {
        var toggle = this.FindControl<Button>("RainbowToggleBtn");
        var card = this.FindControl<Border>("RainbowCard");
        if (toggle == null || card == null) return;

        RebuildRainbowThemes();

        SetupSlider("RainbowSpeedSlider", "RainbowSpeedValue", _rainbowSpeed, v => { _rainbowSpeed = (int)v; ApplyLive(); }, "x");

        toggle.Click += (_, _) =>
        {
            _rainbowCardOpen = !_rainbowCardOpen;
            card.IsVisible = true;
            AnimateRainbowCard(card, _rainbowCardOpen);
            if (_rainbowCardOpen)
            {
                _rainbowEnabled = true;
                toggle.Content = "On";
            }
            if (!_rainbowCardOpen)
            {
                _rainbowEnabled = false;
                toggle.Content = "Rainbow";
                var fadeOut = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                fadeOut.Tick += (_, _) => { if (!_rainbowCardOpen) card.IsVisible = false; fadeOut.Stop(); };
                fadeOut.Start();
            }
            ApplyLive();
        };

        UpdateRainbowPreview();
    }

    private void AnimateRainbowCard(Border card, bool open)
    {
        _rainbowAnimTimer?.Stop();
        double targetH = open ? 130 : 0;
        double targetOp = open ? 1.0 : 0.0;
        double startH = card.MaxHeight;
        double startOp = card.Opacity;
        _rainbowExpandPhase = 0;
        _rainbowAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _rainbowAnimTimer.Tick += (_, _) =>
        {
            _rainbowExpandPhase += 0.08;
            double t = Math.Min(_rainbowExpandPhase, 1.0);
            double ease = 1.0 - Math.Pow(1.0 - t, 3);
            card.MaxHeight = startH + (targetH - startH) * ease;
            card.Opacity = startOp + (targetOp - startOp) * ease;
            if (t >= 1.0) _rainbowAnimTimer?.Stop();
        };
        _rainbowAnimTimer.Start();
    }

    private void UpdateRainbowPreview()
    {
        var bar = this.FindControl<Border>("RainbowPreviewBar");
        if (bar == null) return;
        int themeIdx = Array.IndexOf(_rainbowThemeNames, _rainbowTheme);
        if (themeIdx < 0) themeIdx = 0;
        var colors = _rainbowThemeColors[themeIdx];
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
        };
        for (int i = 0; i < colors.Length; i++)
            gradient.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
        bar.Background = gradient;

        var palette = this.FindControl<WrapPanel>("RainbowPaletteGroup");
        if (palette != null)
        {
            palette.Children.Clear();
            for (int i = 0; i < colors.Length; i++)
            {
                var swatch = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.Parse(colors[i])),
                    BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1)
                };
                palette.Children.Add(swatch);
            }
        }
    }

    private void SyncSlider(string name, double val)
    {
        var s = this.FindControl<Slider>(name);
        if (s != null) s.Value = val;
    }

    private void SyncAllControls()
    {
        _suppressSliderEvents = true;
        SyncSlider("SizeSlider", _size);
        SyncSlider("ThicknessSlider", _thickness);
        SyncSlider("OpacitySlider", _opacity);
        SyncSlider("SpeedSlider", _speed);
        SyncSlider("RotationSlider", _rotation);
        SyncSlider("PosXSlider", _posX);
        SyncSlider("PosYSlider", _posY);
        UpdatePosInputs();
        SyncSlider("SpinSpeedSlider", _spinSpeed);
        SyncSlider("OrbitRadiusSlider", _orbitRadius);
        SyncSlider("OrbitSpeedSlider", _orbitSpeed);
        SyncSlider("RainbowSpeedSlider", _rainbowSpeed);

        SyncText("SizeValue", _size, "px");
        SyncText("ThicknessValue", _thickness, "px");
        SyncText("OpacityValue", _opacity, "%");
        SyncText("SpeedValue", _speed, "x");
        SyncText("RotationValue", _rotation, "");
        SyncText("PosXValue", _posX, "");
        SyncText("PosYValue", _posY, "");
        SyncText("SpinSpeedValue", _spinSpeed, "x");
        SyncText("OrbitRadiusValue", _orbitRadius, "");
        SyncText("OrbitSpeedValue", _orbitSpeed, "x");
        SyncText("RainbowSpeedValue", _rainbowSpeed, "x");

        var bindBtn = this.FindControl<Button>("BindInput");
        if (bindBtn != null) bindBtn.Content = _bindKey;
        var symInput = this.FindControl<TextBox>("SymbolInput");
        if (symInput != null) symInput.Text = _customSymbol;
        var customColor = this.FindControl<TextBox>("CustomColorInput");
        if (customColor != null) customColor.Text = _selectedColor;

        SetVisible("SpinSpeedRow", _spinEnabled);
        SetVisible("SpinDirRow", _spinEnabled);
        SetVisible("OrbitRow", _orbitEnabled);
        SetVisible("OrbitSpeedRow", _orbitEnabled);
        SetVisible("OrbitDirRow", _orbitEnabled);

        SyncButtonGroupActive("PresetGroup", _presetNames, _selectedPreset);
        SyncButtonGroupActive("AnimGroup", _animNames, _selectedAnim);
        var spinBtn = this.FindControl<Button>("SpinBtn");
        if (spinBtn != null) UpdateMotionBtn(spinBtn, _spinEnabled);
        var orbitBtn = this.FindControl<Button>("OrbitBtn");
        if (orbitBtn != null) UpdateMotionBtn(orbitBtn, _orbitEnabled);
        SyncColorButtons();
        UpdateRainbowPreview();



        var rbToggle = this.FindControl<Button>("RainbowToggleBtn");
        if (rbToggle != null) rbToggle.Content = _rainbowEnabled ? "On" : "Rainbow";
        var rbCard = this.FindControl<Border>("RainbowCard");
        if (rbCard != null)
        {
            _rainbowAnimTimer?.Stop();
            _rainbowExpandPhase = _rainbowEnabled ? 1 : 0;
            rbCard.Height = _rainbowEnabled ? 130 : 0;
            rbCard.IsVisible = _rainbowEnabled;
            _rainbowCardOpen = _rainbowEnabled;
        }

        var spinDirBtn = this.FindControl<Button>("SpinDirBtn");
        if (spinDirBtn != null) spinDirBtn.Content = _spinDir == "reverse" ? "Reverse" : "Normal";
        var orbitDirBtn = this.FindControl<Button>("OrbitDirBtn");
        if (orbitDirBtn != null) orbitDirBtn.Content = _orbitDir == "reverse" ? "Reverse" : "Normal";

        _suppressSliderEvents = false;
    }

    private void SyncText(string name, double val, string suffix)
    {
        var tb = SafeFind<TextBlock>(name);
        if (tb != null) { tb.Text = $"{val:0.#}{suffix}"; return; }
        var box = SafeFind<TextBox>(name);
        if (box != null && !box.IsFocused) box.Text = $"{val:0.#}{suffix}";
    }

    private void SyncButtonGroupActive(string panelName, string[] names, string current)
    {
        var panel = this.FindControl<Panel>(panelName);
        if (panel == null) return;
        foreach (var child in panel.Children)
            if (child is Button b)
            {
                if ((string)(b.Tag ?? "") == current) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
            }
    }

    private void ApplyLive()
    {
        UpdateNotebook();
        PushToOverlay();
        StartPreviewAnimation();
        SaveSettings();
    }

    private void PushToOverlay()
    {
        var ch = BuildCurrentState();
        ch.RainbowPalette = BuildPalette();
        _overlayDispatcher?.Invoke(() => _overlayWindow?.UpdateCrosshair(ch));
    }


    private List<string> BuildPalette()
    {
        var palette = new List<string>();
        int themeIdx = Array.IndexOf(_rainbowThemeNames, _rainbowTheme);
        if (themeIdx >= 0 && themeIdx < _rainbowThemeColors.Length)
            palette.AddRange(_rainbowThemeColors[themeIdx]);
        return palette;
    }

    private void SetupButtonGroup(string panelName, string[] names, string[] labels, string current, Action<string> onSelect)
    {
        var panel = this.FindControl<Panel>(panelName);
        if (panel == null) return;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var btn = new Button { Content = labels[i], MinWidth = 42, Height = 28, Tag = name, FontSize = 11 };
            // Stable automation id for UI tests (content labels like "S" are ambiguous).
            AutomationProperties.SetAutomationId(btn, $"Preset_{name}");
            btn.Classes.Add("pill");
            if (name == current) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
            btn.Click += (_, _) =>
            {
                onSelect(name);
                foreach (var child in panel.Children)
                    if (child is Button b)
                    {
                        if ((string)(b.Tag ?? "") == name) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                        else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
                    }
            };
            panel.Children.Add(btn);
        }
    }


    private void RebuildAnimGroup()
    {
        var panel = this.FindControl<Panel>("AnimGroup");
        if (panel == null) return;
        panel.Children.Clear();

        for (int i = 0; i < _animNames.Length; i++)
        {
            var tag = _animNames[i];
            var btn = new Button { Content = tag, MinWidth = 42, Height = 28, Tag = tag, FontSize = 11 };
            btn.Classes.Add("pill");
            if (tag == _selectedAnim) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
            else { btn.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
            btn.Click += (_, _) =>
            {
                _selectedAnim = tag;
                foreach (var child in panel.Children)
                    if (child is Button b)
                    {
                        if ((string)(b.Tag ?? "") == tag) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                        else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
                    }
                ApplyLive();
            };
            panel.Children.Add(btn);
        }
    }

    private void RebuildRainbowThemes()
    {
        var presetsDir = UserPresets.AnimPresetsDir;
        if (!Directory.Exists(presetsDir)) return;

        var names = new System.Collections.Generic.List<string>();
        var labels = new System.Collections.Generic.List<string>();
        var colors = new System.Collections.Generic.List<string[]>();

        foreach (var file in Directory.GetFiles(presetsDir, "*.json"))
        {
            if (System.IO.Path.GetFileName(file).Equals("TEMPLATE.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<AnimPreset>(json);
                if (obj != null && !string.IsNullOrWhiteSpace(obj.Name) && obj.Palette != null)
                {
                    // Drop unparseable colors: one bad entry used to reach
                    // Color.Parse in RebuildPreview and crash the page.
                    var valid = obj.Palette
                        .Where(c => !string.IsNullOrWhiteSpace(c) && Color.TryParse(c, out _))
                        .ToArray();
                    if (valid.Length == 0) continue;

                    names.Add(obj.Name.ToLower());
                    labels.Add(obj.Name);
                    colors.Add(valid);
                }
            }
            catch { }
        }

        _rainbowThemeNames = names.ToArray();
        _rainbowThemeLabels = labels.ToArray();
        _rainbowThemeColors = colors.ToArray();

        var themePanel = this.FindControl<WrapPanel>("RainbowThemeGroup");
        if (themePanel == null) return;
        themePanel.Children.Clear();
        for (int i = 0; i < _rainbowThemeNames.Length; i++)
        {
            var name = _rainbowThemeNames[i];
            var label = _rainbowThemeLabels[i];
            var btn = new Button
            {
                Content = label, MinWidth = 50, Height = 24, Tag = name, FontSize = 10, Classes = { "pill" },
                BorderBrush = new SolidColorBrush(Color.Parse(name == _rainbowTheme ? "#7c5cfc" : "#232329"))
            };
            btn.Click += (_, _) =>
            {
                _rainbowTheme = name;
                foreach (var c in themePanel.Children)
                    if (c is Button tb) tb.BorderBrush = new SolidColorBrush(Color.Parse((string)(tb.Tag ?? "") == name ? "#7c5cfc" : "#232329"));
                UpdateRainbowPreview();
                ApplyLive();
            };
            themePanel.Children.Add(btn);
        }

        if (!Array.Exists(_rainbowThemeNames, n => n == _rainbowTheme) && _rainbowThemeNames.Length > 0)
            _rainbowTheme = _rainbowThemeNames[0];

        UpdateRainbowPreview();
    }

    private void UpdatePosInputs()
    {
        var xBox = SafeFind<TextBox>("PosXValue");
        var yBox = SafeFind<TextBox>("PosYValue");
        if (xBox != null && !xBox.IsFocused) xBox.Text = _posX.ToString("0");
        if (yBox != null && !yBox.IsFocused) yBox.Text = _posY.ToString("0");
    }

    private T? SafeFind<T>(string name) where T : class
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (this.Name == name && this is T self) return self;
        foreach (var d in this.GetVisualDescendants())
            if (d is Control c && c.Name == name)
                return c as T;
        return null;
    }

    private void SetupSlider(string sliderName, string valueName, double initial, Action<double> onChange, string suffix)
    {
        var slider = this.FindControl<Slider>(sliderName);
        var valueBlock = SafeFind<TextBlock>(valueName);
        var valueBox = SafeFind<TextBox>(valueName);
        if (slider == null) return;
        slider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents) return;
            onChange(slider.Value);
            if (valueBlock != null) valueBlock.Text = $"{slider.Value:0.#}{suffix}";
            else if (valueBox != null && !valueBox.IsFocused) valueBox.Text = $"{slider.Value:0.#}{suffix}";
        };
        slider.Value = initial;
        if (valueBlock != null) valueBlock.Text = $"{initial:0.#}{suffix}";
        else if (valueBox != null) valueBox.Text = $"{initial:0.#}{suffix}";

        if (valueBox != null)
        {
            valueBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                if (double.TryParse(valueBox.Text, out var parsed))
                {
                    _suppressSliderEvents = true;
                    slider.Value = Math.Clamp(parsed, slider.Minimum, slider.Maximum);
                    _suppressSliderEvents = false;
                    onChange(slider.Value);
                }
                valueBox.Text = $"{slider.Value:0.#}{suffix}";
            };
        }
    }

    private void UpdateNotebook()
    {
        SetText("NbPreset", $"Preset: {Capitalize(_selectedPreset)}");
        SetText("NbAnimation", $"Animation: {Capitalize(_selectedAnim)}");
        var motions = new List<string>();
        if (_spinEnabled) motions.Add("Spin");
        if (_orbitEnabled) motions.Add("Orbit");
        SetText("NbMotion", $"Motion: {(motions.Count > 0 ? string.Join(" + ", motions) : "None")}");
        SetText("NbSize", $"Size: {_size:0}px");
        SetText("NbThickness", $"Thickness: {_thickness:0.#}px");
        SetText("NbOpacity", $"Opacity: {_opacity:0}%");
        SetText("NbColor", $"Color: {_selectedColor}");
        SetText("NbSpeed", $"Speed: {_speed:0}x");
        SetText("NbPos", $"Position: X {_posX:0} / Y {_posY:0}");
        SetText("NbRotation", $"Rotation: {_rotation:0}");
        SetText("NbBind", $"Bind: {_bindKey}");
    }

    private void SetText(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb != null) tb.Text = text;
    }

    private void StartPreviewAnimation()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
        _animClock.Restart();
        _animPhase = 0;

        RebuildPreview();
        PreviewFrame();

        var s = _previewState;
        bool needsAnim = s != null && (s.Animation != "none" || s.SpinEnabled || s.OrbitEnabled || s.RainbowEnabled);
        if (!needsAnim) return;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _previewTimer.Tick += (_, _) =>
        {
            if (!IsVisible) { _previewTimer?.Stop(); _previewTimer = null; return; }
            _animPhase = _animClock.Elapsed.TotalSeconds;
            PreviewFrame();
        };
        _previewTimer.Start();
    }


    private void RebuildPreview()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();
        _previewBrushes.Clear();
        _previewShapes = null;
        _previewHalo = null;
        _previewOuterTg = null;
        _previewInnerTg = null;
        _previewOrbit = null;
        _previewSpin = null;
        _previewScale = null;

        var state = BuildCurrentState();
        _previewState = state;


        var palette = BuildPalette();
        _previewPaletteCount = palette.Count;
        _previewPaletteRgb = new byte[palette.Count * 3];
        for (int i = 0; i < palette.Count; i++)
        {
            // TryParse instead of Parse: a malformed palette entry must not
            // take down the whole preview loop.
            if (!Color.TryParse(palette[i], out var pc)) pc = Color.Parse("#22c55e");
            _previewPaletteRgb[i * 3] = pc.R;
            _previewPaletteRgb[i * 3 + 1] = pc.G;
            _previewPaletteRgb[i * 3 + 2] = pc.B;
        }

        const double center = 150.0;
        double posX = center + state.PosX;
        double posY = center + state.PosY;


        double cx = posX + (state.OrbitEnabled ? state.OrbitRadius : 0);
        double cy = posY;

        var brush = new SolidColorBrush(Color.Parse(SafeColor(state.Color)));
        _previewBrushes.Add(brush);

        double t = CrosshairGeometry.Thickness(state.Thickness);
        double sz = state.Size;

        var shapes = new Canvas { Width = 300, Height = 300 };


        void DrawFigure(Canvas target)
        {
            switch (state.Preset)
            {
                case "cross":
                {
                    var bars = new List<Bar>(2);
                    CrosshairGeometry.CrossBars(cx, cy, sz, t, bars);
                    AddBars(target, bars, brush);
                    break;
                }
                case "bracket":
                {
                    var bars = new List<Bar>(8);
                    CrosshairGeometry.BracketBars(cx, cy, sz, t, bars);
                    AddBars(target, bars, brush);
                    break;
                }
                case "circle":
                {
                    double d = CrosshairGeometry.CircleDiameter(sz);
                    var el = new Ellipse { Width = d, Height = d, Stroke = brush, StrokeThickness = t };
                    Canvas.SetLeft(el, Snap(cx - d / 2.0));
                    Canvas.SetTop(el, Snap(cy - d / 2.0));
                    target.Children.Add(el);
                    break;
                }
                case "custom":
                {
                    var el = new TextBlock
                    {
                        Text = _customSymbol,
                        FontSize = CrosshairGeometry.SymbolFontSize(sz),
                        Foreground = brush
                    };
                    el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(el, Snap(cx - el.DesiredSize.Width / 2.0));
                    Canvas.SetTop(el, Snap(cy - el.DesiredSize.Height / 2.0));
                    target.Children.Add(el);
                    break;
                }
                case "dot":
                default:
                {
                    double d = CrosshairGeometry.DotSize(sz);
                    var el = new Ellipse { Width = d, Height = d, Fill = brush };
                    Canvas.SetLeft(el, Snap(cx - d / 2.0));
                    Canvas.SetTop(el, Snap(cy - d / 2.0));
                    target.Children.Add(el);
                    break;
                }
            }
        }





        _previewHalo = null;
        if (state.Animation == "glow")
        {
            var halo = new Canvas { Width = 300, Height = 300 };
            foreach (var (ringScale, ringOpacity) in CrosshairGeometry.GlowRings)
            {
                var ring = new Canvas { Width = 300, Height = 300, Opacity = ringOpacity };
                DrawFigure(ring);

                ring.RenderTransformOrigin = new RelativePoint(cx, cy, RelativeUnit.Absolute);
                ring.RenderTransform = new ScaleTransform(ringScale, ringScale);
                halo.Children.Add(ring);
            }
            halo.Opacity = CrosshairGeometry.GlowHaloMax;
            shapes.Children.Add(halo);
            _previewHalo = halo;
        }

        DrawFigure(shapes);



        _previewSpin = new RotateTransform();
        _previewScale = new ScaleTransform(1, 1);
        _previewInnerTg = new TransformGroup();
        _previewInnerTg.Children.Add(_previewSpin);
        _previewInnerTg.Children.Add(_previewScale);
        shapes.RenderTransformOrigin = new RelativePoint(cx, cy, RelativeUnit.Absolute);
        shapes.RenderTransform = _previewInnerTg;



        _previewOrbit = new RotateTransform();
        _previewOuterTg = new TransformGroup();
        _previewOuterTg.Children.Add(_previewOrbit);
        var outer = new Canvas { Width = 300, Height = 300 };
        outer.RenderTransformOrigin = new RelativePoint(posX, posY, RelativeUnit.Absolute);
        outer.RenderTransform = _previewOuterTg;
        outer.Children.Add(shapes);

        _previewShapes = shapes;
        _canvas.Children.Add(outer);
    }

    private void AddBars(Canvas shapes, List<Bar> bars, IBrush brush)
    {
        foreach (var b in bars)
        {
            var r = new Rectangle { Width = Snap(b.W), Height = Snap(b.H), Fill = brush };
            Canvas.SetLeft(r, Snap(b.X));
            Canvas.SetTop(r, Snap(b.Y));
            shapes.Children.Add(r);
        }
    }


    private void PreviewFrame()
    {
        var s = _previewState;
        var shapes = _previewShapes;
        if (s == null || shapes == null || _previewOuterTg == null || _previewInnerTg == null
            || _previewOrbit == null || _previewSpin == null || _previewScale == null) return;

        double t = _animPhase;
        double animScale = 1.0;
        double animOpacity = s.Opacity / 100.0;

        if (s.Animation != "none")
        {
            double dur = s.Animation == "scale"
                ? CrosshairGeometry.ScaleDuration
                : CrosshairGeometry.AnimDuration(s.Speed);
            double u = CrosshairGeometry.SmoothWave(t, dur);
            switch (s.Animation)
            {
                case "pulse":
                    animScale = CrosshairGeometry.PulseFrom + u * (CrosshairGeometry.PulseTo - CrosshairGeometry.PulseFrom);
                    break;
                case "scale":
                    animScale = CrosshairGeometry.ScaleFrom + u * (CrosshairGeometry.ScaleTo - CrosshairGeometry.ScaleFrom);
                    break;
                case "glow":

                    if (_previewHalo != null)
                        _previewHalo.Opacity = CrosshairGeometry.GlowHaloMin
                            + u * (CrosshairGeometry.GlowHaloMax - CrosshairGeometry.GlowHaloMin);
                    break;
            }
        }



        double posAngle = s.Rotation;
        if (s.OrbitEnabled)
        {
            double phase = (t / CrosshairGeometry.OrbitDuration(s.OrbitSpeed)) * 360.0;
            posAngle += s.OrbitDir == "reverse" ? -phase : phase;
        }

        double shapeAngle = 0;
        if (s.SpinEnabled)
        {
            double phase = (t / CrosshairGeometry.SpinDuration(s.SpinSpeed)) * 360.0;
            shapeAngle = s.SpinDir == "reverse" ? -phase : phase;
        }

        if (s.RainbowEnabled && _previewPaletteCount > 0)
        {
            CrosshairGeometry.PaletteColor(_previewPaletteRgb, _previewPaletteCount, t,
                CrosshairGeometry.RainbowCycleDuration(s.RainbowSpeed), out byte rr, out byte gg, out byte bb);
            var c = Color.FromRgb(rr, gg, bb);
            foreach (var b in _previewBrushes) b.Color = c;
        }

        shapes.Opacity = animOpacity;
        _previewOrbit.Angle = posAngle;
        _previewSpin.Angle = shapeAngle;
        _previewScale.ScaleX = _previewScale.ScaleY = animScale;


        var outer = shapes.Parent as Canvas;
        if (outer != null)
            outer.RenderTransform = Math.Abs(NormAngle(posAngle)) < 0.01 ? null : _previewOuterTg;
        shapes.RenderTransform =
            Math.Abs(NormAngle(shapeAngle)) < 0.01 && Math.Abs(animScale - 1.0) < 0.0001 ? null : _previewInnerTg;
    }


    private static double NormAngle(double angle)
    {
        double norm = angle % 360.0;
        if (norm <= -180.0) norm += 360.0;
        else if (norm > 180.0) norm -= 360.0;
        return norm;
    }


    private double Snap(double v)
    {
        double scale = 1.0;
        try { scale = TopLevel.GetTopLevel(_canvas)?.RenderScaling ?? 1.0; } catch { }
        if (scale <= 0) scale = 1.0;
        return Math.Round(v * scale, MidpointRounding.AwayFromZero) / scale;
    }
    private void ResetDefaults()
    {
        var def = new CrosshairState();
        _selectedPreset = def.Preset; _selectedAnim = def.Animation; _spinEnabled = def.SpinEnabled; _orbitEnabled = def.OrbitEnabled;
        _selectedColor = def.Color; _size = def.Size; _thickness = def.Thickness; _opacity = def.Opacity;
        _speed = def.Speed; _rotation = def.Rotation; _posX = def.PosX; _posY = def.PosY;
        _spinSpeed = def.SpinSpeed; _spinDir = def.SpinDir;
        _orbitRadius = def.OrbitRadius; _orbitSpeed = def.OrbitSpeed; _orbitDir = def.OrbitDir;
        _rainbowEnabled = def.RainbowEnabled;
        _rainbowSpeed = def.RainbowSpeed; _rainbowTheme = def.RainbowTheme;
        _rainbowCardOpen = false;
        _bindKey = def.BindKey; _customSymbol = def.Symbol;
    }

    private void SaveSettings()
    {
        var ch = new CrosshairState
        {
            Preset = _selectedPreset, Animation = _selectedAnim, SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
            Color = _selectedColor, Size = (int)_size, Thickness = _thickness, Opacity = (int)_opacity,
            Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
            SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
            OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
            RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
            BindKey = _bindKey, Symbol = _customSymbol
        };
        _settings.SaveCrosshair(ch);
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
