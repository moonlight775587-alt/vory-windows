using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VoryWindows.Views.Controls
{
    /// <summary>
    /// Drawn bot face: per-bot color head with a simple blink animation.
    /// Motion rule (from the original): calm motion only - no bounce or scale pops.
    /// </summary>
    public partial class AvatarControl : UserControl
    {
        private readonly DispatcherTimer _blinkTimer;
        private readonly Random _rand = new Random();

        public static readonly DependencyProperty BotColorProperty =
            DependencyProperty.Register("BotColor", typeof(string), typeof(AvatarControl),
                new PropertyMetadata("#5B8DEF", OnBotColorChanged));

        public string BotColor
        {
            get { return (string)GetValue(BotColorProperty); }
            set { SetValue(BotColorProperty, value); }
        }

        public AvatarControl()
        {
            InitializeComponent();
            ApplyColor(BotColor);
            _blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2800)
            };
            _blinkTimer.Tick += (s, e) => Blink();
            _blinkTimer.Start();
            Unloaded += (s, e) => _blinkTimer.Stop();
        }

        private static void OnBotColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((AvatarControl)d).ApplyColor(e.NewValue as string);
        }

        private void ApplyColor(string spec)
        {
            try
            {
                Color c;
                if (!string.IsNullOrEmpty(spec) && spec.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
                    c = HslToColor(spec);
                else
                    c = (Color)ColorConverter.ConvertFromString(spec ?? "#5B8DEF");
                Head.Fill = new SolidColorBrush(c);
            }
            catch { /* keep default */ }
        }

        private static Color HslToColor(string hsl)
        {
            // "hsl(210,55%,55%)"
            var inner = hsl.Substring(hsl.IndexOf('(') + 1).TrimEnd(')', ' ');
            var parts = inner.Split(',');
            double h = double.Parse(parts[0].Trim());
            double s = double.Parse(parts[1].Trim().TrimEnd('%')) / 100.0;
            double l = double.Parse(parts[2].Trim().TrimEnd('%')) / 100.0;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private async void Blink()
        {
            // Calm blink: eyes narrow briefly. No scale pops.
            _blinkTimer.Interval = TimeSpan.FromMilliseconds(_rand.Next(2200, 5200));
            EyeL.Height = 2; EyeR.Height = 2;
            EyeL.SetValue(Canvas.TopProperty, 17.0);
            EyeR.SetValue(Canvas.TopProperty, 17.0);
            await System.Threading.Tasks.Task.Delay(140);
            EyeL.Height = 8; EyeR.Height = 8;
            EyeL.SetValue(Canvas.TopProperty, 14.0);
            EyeR.SetValue(Canvas.TopProperty, 14.0);
        }
    }
}
