using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FloatingClock
{
    // The Planner's jade chapter banner, fine gold rings and rounded outline,
    // rendered in the existing layered window with no animation or image assets.
    internal sealed class SmtvvClockChrome : FrameworkElement
    {
        public const double Radius = 14;
        private bool showDate;
        private readonly Typeface labels = new Typeface(ClockTypography.Create(8),
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        private readonly Typeface heading = new Typeface(ClockTypography.SmtvvHeading(),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Brush Gold = Solid(198, 174, 118, 255);
        private static readonly Brush Jade = Solid(160, 221, 208, 255);
        private static readonly Pen Ring = new Pen(Solid(198, 174, 118, 56), 0.65);
        private static readonly Pen InnerRing = new Pen(Solid(198, 174, 118, 28), 0.65);
        private static readonly Pen Inset = new Pen(Solid(248, 244, 223, 25), 0.65);

        public SmtvvClockChrome() { IsHitTestVisible = false; }

        public void Configure(bool date)
        {
            showDate = date;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            drawing.DrawRoundedRectangle(null, Inset, new Rect(3, 3, w - 6, h - 6), Radius - 3, Radius - 3);

            // Keep the rings in the outer margins so they never cross the digits.
            GeometryGroup margins = new GeometryGroup();
            margins.Children.Add(new RectangleGeometry(new Rect(0, 0, w, 15)));
            margins.Children.Add(new RectangleGeometry(new Rect(0, h - 15, w, 15)));
            margins.Children.Add(new RectangleGeometry(new Rect(w - 9, 15, 9, h - 30)));
            drawing.PushClip(margins);
            Point center = new Point(w - 25, h / 2);
            drawing.DrawEllipse(null, Ring, center, 41, 41);
            drawing.DrawEllipse(null, InnerRing, center, 35, 35);
            drawing.Pop();

            Label(drawing, "LOCAL TIME", labels, w / 2, 5, 6.5, Jade);
            Label(drawing, "SMT V V", heading, w / 2, h - 11.5, 6.7, Gold);
            if (showDate)
            {
                Label(drawing, "YEAR", labels, ClockLayout.DateColumnWidth / 2, 8, 6, Gold);
                drawing.DrawLine(new Pen(Gold, 0.7), new Point(17, h - 16), new Point(23, h - 16));
            }
        }

        private static void Label(DrawingContext drawing, string text, Typeface face,
            double x, double y, double size, Brush ink)
        {
            FormattedText label = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, face, size, ink, 1.0);
            drawing.DrawText(label, new Point(x - label.Width / 2, y));
        }

        private static SolidColorBrush Solid(byte r, byte g, byte b, byte alpha)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
