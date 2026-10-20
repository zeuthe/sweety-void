using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class CrosshairRenderer
    {
        private readonly Canvas _canvas;
        private FrameworkElement _currentElement;
        private FrameworkElement _glowElement;
        private Storyboard _animStoryboard;
        private Storyboard _motionStoryboard;
        private readonly List<SolidColorBrush> _rainbowBrushes = new();

        public CrosshairRenderer(Canvas canvas)
        {
            _canvas = canvas;
        }

        private FrameworkElement CreatePreset(CrosshairState state)
        {
            return state.Preset switch
            {
                "dot" => CreateDot(state),
                "cross" => CreateCross(state),
                "circle" => CreateCircle(state),
                "bracket" => CreateBracket(state),

                "symbol" => CreateSymbol(state),
                "custom" => CreateSymbol(state),
                _ => CreateDot(state)
            };
        }


        private void Place(FrameworkElement element, double cx, double cy)
        {
            if (element is Canvas cvs)
            {
                Canvas.SetLeft(element, Snap(cx - cvs.Width / 2.0));
                Canvas.SetTop(element, Snap(cy - cvs.Height / 2.0));
            }
            else if (element is Ellipse el)
            {
                Canvas.SetLeft(element, Snap(cx - el.Width / 2.0));
                Canvas.SetTop(element, Snap(cy - el.Height / 2.0));
            }
            else if (element is TextBlock tb)
            {
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var tbw = tb.DesiredSize.Width;
                var tbh = tb.DesiredSize.Height;
                Canvas.SetLeft(element, Snap(cx - tbw / 2.0));
                Canvas.SetTop(element, Snap(cy - tbh / 2.0));
            }
            else
            {
                Canvas.SetLeft(element, Snap(cx));
                Canvas.SetTop(element, Snap(cy));
            }
        }

        public void Render(CrosshairState state)
        {
            try
            {
                RenderCore(state);
            }
            catch (Exception ex)
            {


                CrashLog.Write("Overlay.Render", ex);
            }
        }

        private void RenderCore(CrosshairState state)
        {
            _canvas.Children.Clear();
            _animStoryboard?.Stop();
            _animStoryboard = null;
            _motionStoryboard?.Stop();
            _motionStoryboard = null;
            _canvas.RenderTransform = null;
            _rainbowBrushes.Clear();


            double canvasW = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
            double canvasH = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : SystemParameters.PrimaryScreenHeight;
            double cx = canvasW / 2.0 + state.PosX;
            double cy = canvasH / 2.0 + state.PosY;

            _currentElement = CreatePreset(state);
            if (_currentElement == null) return;




            _glowElement = null;
            if (state.Animation == "glow")
            {
                _glowElement = CreateGlowHalo(state);
                if (_glowElement != null)
                {
                    Place(_glowElement, cx, cy);
                    _canvas.Children.Add(_glowElement);
                }
            }

            Place(_currentElement, cx, cy);
            _canvas.Children.Add(_currentElement);



            if (Math.Abs(state.Rotation) > 0.01)
            {
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new RotateTransform(state.Rotation, cx, cy));
                _canvas.RenderTransform = transformGroup;
                _canvas.RenderTransformOrigin = new Point(0, 0);
            }

            ApplyAnimations(state, cx, cy);
            ApplyMotions(state, cx, cy);
            if (state.RainbowEnabled && state.RainbowPalette.Count >= 2)
                ApplyRainbow(state);
        }

        public void Clear()
        {
            _animStoryboard?.Stop();
            _motionStoryboard?.Stop();
            _animStoryboard = null;
            _motionStoryboard = null;
            _canvas.Children.Clear();
            _canvas.RenderTransform = null;
            _rainbowBrushes.Clear();
            _currentElement = null;
            _glowElement = null;
        }

        /// <summary>


        /// </summary>
        private double Snap(double value)
        {
            double scale = 1.0;
            try { scale = System.Windows.Media.VisualTreeHelper.GetDpi(_canvas).PixelsPerDip; }
            catch { }
            if (scale <= 0) scale = 1.0;
            return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        }

        private FrameworkElement CreateDot(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            return new Ellipse
            {
                Width = CrosshairGeometry.DotSize(state.Size),
                Height = CrosshairGeometry.DotSize(state.Size),
                Fill = brush,
                Opacity = state.Opacity / 100.0
            };
        }

        private FrameworkElement CreateCross(CrosshairState state)
        {
            double center = 50.0;
            var c = new Canvas { Width = 100, Height = 100 };
            var hBrush = GetBrush(state.Color);
            var vBrush = GetBrush(state.Color);
            _rainbowBrushes.Add(hBrush);
            _rainbowBrushes.Add(vBrush);
            double t = CrosshairGeometry.Thickness(state.Thickness);


            var bars = new List<Bar>();
            CrosshairGeometry.CrossBars(center, center, state.Size, t, bars);
            int idx = 0;
            foreach (var b in bars)
            {
                var r = new Rectangle
                {
                    Width = Snap(b.W),
                    Height = Snap(b.H),
                    Fill = idx == 0 ? hBrush : vBrush,
                    Opacity = state.Opacity / 100.0
                };
                Canvas.SetLeft(r, Snap(b.X));
                Canvas.SetTop(r, Snap(b.Y));
                c.Children.Add(r);
                idx++;
            }
            return c;
        }

        private FrameworkElement CreateCircle(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            double t = CrosshairGeometry.Thickness(state.Thickness);
            return new Ellipse
            {
                Width = CrosshairGeometry.CircleDiameter(state.Size),
                Height = CrosshairGeometry.CircleDiameter(state.Size),
                Stroke = brush,
                StrokeThickness = t,
                Opacity = state.Opacity / 100.0
            };
        }

        private FrameworkElement CreateBracket(CrosshairState state)
        {
            double center = 50.0;
            var c = new Canvas { Width = 100, Height = 100 };
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            double t = CrosshairGeometry.Thickness(state.Thickness);
            double o = state.Opacity / 100.0;

            var bars = new List<Bar>();
            CrosshairGeometry.BracketBars(center, center, state.Size, t, bars);
            foreach (var b in bars)
            {
                var r = new Rectangle
                {
                    Width = Snap(b.W),
                    Height = Snap(b.H),
                    Fill = brush,
                    Opacity = o
                };
                Canvas.SetLeft(r, Snap(b.X));
                Canvas.SetTop(r, Snap(b.Y));
                c.Children.Add(r);
            }
            return c;
        }

        private FrameworkElement CreateSymbol(CrosshairState state)
        {
            var brush = GetBrush(state.Color);
            _rainbowBrushes.Add(brush);
            return new TextBlock
            {
                Text = state.Symbol,
                FontSize = CrosshairGeometry.SymbolFontSize(state.Size),
                Foreground = brush,
                Opacity = state.Opacity / 100.0,
                TextAlignment = TextAlignment.Center,
                // Fallback chain: symbols the base font lacks render from
                // Segoe UI Symbol/Emoji instead of empty boxes.
                FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol, Segoe UI Emoji")
            };
        }


        private static System.Windows.Size ElementSize(FrameworkElement element)
        {
            if (element is Canvas cvs)
                return new System.Windows.Size(double.IsNaN(cvs.Width) ? 0 : cvs.Width,
                                                double.IsNaN(cvs.Height) ? 0 : cvs.Height);
            if (!double.IsNaN(element.Width) && !double.IsNaN(element.Height))
                return new System.Windows.Size(element.Width, element.Height);

            element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize;
        }

        /// <summary>




        /// </summary>
        private FrameworkElement CreateGlowHalo(CrosshairState state)
        {
            const double box = 400.0;
            double half = box / 2.0;

            var halo = new Canvas { Width = box, Height = box };
            bool any = false;

            foreach (var (ringScale, ringOpacity) in CrosshairGeometry.GlowRings)
            {
                var preset = CreatePreset(state);
                if (preset == null) continue;

                var sz = ElementSize(preset);



                var ring = new Canvas
                {
                    Width = sz.Width,
                    Height = sz.Height,
                    Opacity = ringOpacity
                };
                Canvas.SetLeft(preset, 0);
                Canvas.SetTop(preset, 0);
                ring.Children.Add(preset);
                ring.RenderTransformOrigin = new Point(0.5, 0.5);
                ring.RenderTransform = new ScaleTransform(ringScale, ringScale);

                Canvas.SetLeft(ring, Snap(half - sz.Width / 2.0));
                Canvas.SetTop(ring, Snap(half - sz.Height / 2.0));
                halo.Children.Add(ring);
                any = true;
            }

            if (!any) return null;
            halo.Opacity = CrosshairGeometry.GlowHaloMax;
            return halo;
        }

        private void ApplyAnimations(CrosshairState state, double cx, double cy)
        {
            _animStoryboard = new Storyboard();
            var duration = new Duration(TimeSpan.FromSeconds(CrosshairGeometry.AnimDuration(state.Speed)));

            if (_currentElement is FrameworkElement fe)
            {




                fe.RenderTransformOrigin = new Point(0.5, 0.5);
                switch (state.Animation)
                {
                    case "pulse":
                    {
                        var tg = new TransformGroup();
                        fe.RenderTransform = tg;
                        AddScaleAnimation(fe, tg, duration, CrosshairGeometry.PulseFrom, CrosshairGeometry.PulseTo);
                        break;
                    }
                    case "scale":
                    {
                        var tg = new TransformGroup();
                        fe.RenderTransform = tg;
                        AddScaleAnimation(fe, tg, new Duration(TimeSpan.FromSeconds(CrosshairGeometry.ScaleDuration)), CrosshairGeometry.ScaleFrom, CrosshairGeometry.ScaleTo);
                        break;
                    }
                    case "glow":




                        if (_glowElement != null)
                        {
                            AddOpacityAnimation(_glowElement, duration,
                                CrosshairGeometry.GlowHaloMin,
                                CrosshairGeometry.GlowHaloMax);
                        }
                        break;
                }
            }

            if (_animStoryboard.Children.Count > 0)
            {
                _animStoryboard.RepeatBehavior = RepeatBehavior.Forever;
                _animStoryboard.Begin(_canvas);
            }
        }

        private void ApplyMotions(CrosshairState state, double cx, double cy)
        {
            if (!state.SpinEnabled && !state.OrbitEnabled) return;

            _motionStoryboard = new Storyboard();

            var existingTg = _canvas.RenderTransform as TransformGroup;
            if (existingTg == null)
            {
                existingTg = new TransformGroup();
                _canvas.RenderTransform = existingTg;
            }

            int transformIdx = existingTg.Children.Count;



            var targets = _glowElement != null
                ? new List<FrameworkElement> { _glowElement, _currentElement }
                : new List<FrameworkElement> { _currentElement };

            if (state.SpinEnabled)
            {
                var spinDur = new Duration(TimeSpan.FromSeconds(CrosshairGeometry.SpinDuration(state.SpinSpeed)));



                bool spinReverse = state.SpinDir == "reverse";

                foreach (var target in targets)
                {
                    if (target == null) continue;


                    target.RenderTransformOrigin = new Point(0.5, 0.5);
                    var spinAnim = new DoubleAnimation(spinReverse ? 360 : 0, spinReverse ? 0 : 360, spinDur)
                    {
                        RepeatBehavior = RepeatBehavior.Forever
                    };

                    var feTg = target.RenderTransform as TransformGroup;
                    if (feTg == null)
                    {
                        feTg = new TransformGroup();
                        target.RenderTransform = feTg;
                    }
                    var rotateTransform = new RotateTransform(0);
                    feTg.Children.Add(rotateTransform);

                    _motionStoryboard.Children.Add(spinAnim);
                    Storyboard.SetTarget(spinAnim, target);
                    Storyboard.SetTargetProperty(spinAnim, new PropertyPath(
                        $"(UIElement.RenderTransform).(TransformGroup.Children)[{feTg.Children.Count - 1}].(RotateTransform.Angle)"));
                }
            }

            if (state.OrbitEnabled)
            {
                var orbitDur = new Duration(TimeSpan.FromSeconds(CrosshairGeometry.OrbitDuration(state.OrbitSpeed)));
                double radius = state.OrbitRadius;


                bool orbitReverse = state.OrbitDir == "reverse";
                var orbitAngleAnim = new DoubleAnimation(orbitReverse ? 360 : 0, orbitReverse ? 0 : 360, orbitDur)
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };

                var orbitTransform = new RotateTransform(0, cx, cy);
                existingTg.Children.Add(orbitTransform);

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    var feTg = target.RenderTransform as TransformGroup;
                    if (feTg == null)
                    {
                        feTg = new TransformGroup();
                        target.RenderTransform = feTg;
                    }
                    feTg.Children.Add(new TranslateTransform(radius, 0));
                }

                _motionStoryboard.Children.Add(orbitAngleAnim);
                Storyboard.SetTarget(orbitAngleAnim, _canvas);
                Storyboard.SetTargetProperty(orbitAngleAnim, new PropertyPath(
                    $"(UIElement.RenderTransform).(TransformGroup.Children)[{transformIdx}].(RotateTransform.Angle)"));
            }

            if (_motionStoryboard.Children.Count > 0)
            {
                _motionStoryboard.RepeatBehavior = RepeatBehavior.Forever;
                _motionStoryboard.Begin(_canvas);
            }
        }

        private void ApplyRainbow(CrosshairState state)
        {
            var palette = state.RainbowPalette;
            int count = palette.Count;
            var colors = palette.Select(hex => (Color)ColorConverter.ConvertFromString(hex)).ToList();
            double cycleDur = CrosshairGeometry.RainbowCycleDuration(state.RainbowSpeed);
            var totalDur = new Duration(TimeSpan.FromSeconds(cycleDur * count));

            foreach (var brush in _rainbowBrushes)
            {
                if (brush == null) continue;
                var ca = new ColorAnimationUsingKeyFrames();
                ca.Duration = totalDur;
                ca.RepeatBehavior = RepeatBehavior.Forever;

                for (int i = 0; i < colors.Count; i++)
                {
                    var color = colors[i];
                    double pct = (double)i / colors.Count;
                    ca.KeyFrames.Add(new LinearColorKeyFrame(color, KeyTime.FromPercent(pct)));
                }

                brush.BeginAnimation(SolidColorBrush.ColorProperty, ca);
            }
        }

        private static SolidColorBrush GetBrush(string hex)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#00ff00";
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Green); }
        }

        private void AddScaleAnimation(FrameworkElement element, TransformGroup tg, Duration duration, double from, double to)
        {
            var scale = new ScaleTransform(1, 1);
            tg.Children.Add(scale);
            int idx = tg.Children.Count - 1;



            var animX = new DoubleAnimation(from, to, duration)
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animX, element);
            Storyboard.SetTargetProperty(animX, new PropertyPath(
                $"(UIElement.RenderTransform).(TransformGroup.Children)[{idx}].(ScaleTransform.ScaleX)"));
            _animStoryboard.Children.Add(animX);

            var animY = new DoubleAnimation(from, to, duration)
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animY, element);
            Storyboard.SetTargetProperty(animY, new PropertyPath(
                $"(UIElement.RenderTransform).(TransformGroup.Children)[{idx}].(ScaleTransform.ScaleY)"));
            _animStoryboard.Children.Add(animY);
        }

        private void AddOpacityAnimation(FrameworkElement element, Duration duration, double from, double to)
        {
            var anim = new DoubleAnimation(from, to, duration)
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, element);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            _animStoryboard.Children.Add(anim);
        }
    }
}
