using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VoidUI.Models;
using VoidUI.Native;
using VoidUI.Services;

namespace VoidUI
{
    public partial class OverlayWindow : Window
    {
        private readonly CrosshairRenderer _renderer;
        private readonly DispatcherTimer _trackTimer;
        private IntPtr _overlayHwnd;
        private IntPtr _rustHwnd;
        private CrosshairState _pendingState;
        private CrosshairState _lastState;
        private bool _renderPending;

        private void Log(string msg)
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay_debug.log");
                System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [Overlay] {msg}\n");
            }
            catch { }
        }

        public OverlayWindow(SettingsService settings)
        {
            InitializeComponent();
            Log("Constructor called");
            _renderer = new CrosshairRenderer(CrosshairCanvas);
            _trackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _trackTimer.Tick += OnTrackTick;
            CrosshairCanvas.SizeChanged += (_, e) => RequestRerender();
        }

        private void RequestRerender()
        {
            if (_lastState == null || _renderPending) return;
            _renderPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _renderPending = false;
                if (_lastState != null && IsLoaded)
                    _renderer.Render(_lastState);
            }));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log("OnLoaded fired");
            _overlayHwnd = new WindowInteropHelper(this).Handle;
            Log($"HWND={_overlayHwnd}");

            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Log($"Size set: {Width}x{Height}");

            MakeClickthrough();
            Log("MakeClickthrough done");
            _trackTimer.Start();

            if (_pendingState != null)
            {
                Log($"Rendering pending state: preset={_pendingState.Preset}, size={_pendingState.Size}");
                _lastState = _pendingState;
                _renderer.Render(_pendingState);
                _pendingState = null;
                Log("Pending state rendered");
            }
            else
            {
                Log("No pending state, loading from settings");
                try
                {
                    var settings = new SettingsService();
                    var ch = settings.GetSettings().Crosshair;
                    if (ch != null)
                    {
                        _lastState = ch;
                        _renderer.Render(ch);
                        Log($"Rendered from settings: preset={ch.Preset}");
                    }
                }
                catch (Exception ex) { Log($"Failed to load settings: {ex.Message}"); }
            }
        }

        private void OnTrackTick(object sender, EventArgs e)
        {


            try
            {
                if (_rustHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_rustHwnd))
                    _rustHwnd = NativeMethods.FindWindow("UnityWndClass", null);

                if (_rustHwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(_rustHwnd))
                {
                    NativeMethods.GetWindowRect(_rustHwnd, out var rect);

                    double scale = 1.0;
                    try { scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                    if (scale <= 0) scale = 1.0;
                    Left = rect.Left / scale;
                    Top = rect.Top / scale;
                    Width = (rect.Right - rect.Left) / scale;
                    Height = (rect.Bottom - rect.Top) / scale;
                }
                else
                {
                    Left = 0;
                    Top = 0;
                    Width = SystemParameters.PrimaryScreenWidth;
                    Height = SystemParameters.PrimaryScreenHeight;
                }
            }
            catch (Exception ex)
            {
                Log($"OnTrackTick failed: {ex.Message}");
            }
        }

        private void MakeClickthrough()
        {
            try
            {
                var exStyle = NativeMethods.GetWindowLong(_overlayHwnd, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(_overlayHwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            catch { }
        }

        public void UpdateCrosshair(CrosshairState state)
        {
            _lastState = state;
            if (IsLoaded)
                _renderer.Render(state);
            else
                _pendingState = state;
        }
    }
}
