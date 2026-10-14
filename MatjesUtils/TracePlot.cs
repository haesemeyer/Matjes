using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace MatjesUtils
{
    /// <summary>
    /// Lightweight line plot of up to two traces with two optional highlighted x-ranges:
    /// a filled one (e.g. a valid window) and an outlined one (e.g. a user selection)
    /// </summary>
    public class TracePlot : FrameworkElement
    {
        #region Dependency properties

        public IReadOnlyList<Point>? Trace1
        {
            get { return (IReadOnlyList<Point>?)GetValue(Trace1Property); }
            set { SetValue(Trace1Property, value); }
        }
        public static readonly DependencyProperty Trace1Property = DependencyProperty.Register(nameof(Trace1), typeof(IReadOnlyList<Point>), typeof(TracePlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<Point>? Trace2
        {
            get { return (IReadOnlyList<Point>?)GetValue(Trace2Property); }
            set { SetValue(Trace2Property, value); }
        }
        public static readonly DependencyProperty Trace2Property = DependencyProperty.Register(nameof(Trace2), typeof(IReadOnlyList<Point>), typeof(TracePlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Trace1Brush
        {
            get { return (Brush)GetValue(Trace1BrushProperty); }
            set { SetValue(Trace1BrushProperty, value); }
        }
        public static readonly DependencyProperty Trace1BrushProperty = DependencyProperty.Register(nameof(Trace1Brush), typeof(Brush), typeof(TracePlot), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Trace2Brush
        {
            get { return (Brush)GetValue(Trace2BrushProperty); }
            set { SetValue(Trace2BrushProperty, value); }
        }
        public static readonly DependencyProperty Trace2BrushProperty = DependencyProperty.Register(nameof(Trace2Brush), typeof(Brush), typeof(TracePlot), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Start of the filled x-range, NaN for none
        /// </summary>
        public double FillStart
        {
            get { return (double)GetValue(FillStartProperty); }
            set { SetValue(FillStartProperty, value); }
        }
        public static readonly DependencyProperty FillStartProperty = DependencyProperty.Register(nameof(FillStart), typeof(double), typeof(TracePlot), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// End of the filled x-range, NaN for none
        /// </summary>
        public double FillEnd
        {
            get { return (double)GetValue(FillEndProperty); }
            set { SetValue(FillEndProperty, value); }
        }
        public static readonly DependencyProperty FillEndProperty = DependencyProperty.Register(nameof(FillEnd), typeof(double), typeof(TracePlot), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush FillBrush
        {
            get { return (Brush)GetValue(FillBrushProperty); }
            set { SetValue(FillBrushProperty, value); }
        }
        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(TracePlot), new FrameworkPropertyMetadata(DefaultFillBrush(), FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Start of the outlined x-range, NaN for none
        /// </summary>
        public double OutlineStart
        {
            get { return (double)GetValue(OutlineStartProperty); }
            set { SetValue(OutlineStartProperty, value); }
        }
        public static readonly DependencyProperty OutlineStartProperty = DependencyProperty.Register(nameof(OutlineStart), typeof(double), typeof(TracePlot), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// End of the outlined x-range, NaN for none
        /// </summary>
        public double OutlineEnd
        {
            get { return (double)GetValue(OutlineEndProperty); }
            set { SetValue(OutlineEndProperty, value); }
        }
        public static readonly DependencyProperty OutlineEndProperty = DependencyProperty.Register(nameof(OutlineEnd), typeof(double), typeof(TracePlot), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush OutlineBrush
        {
            get { return (Brush)GetValue(OutlineBrushProperty); }
            set { SetValue(OutlineBrushProperty, value); }
        }
        public static readonly DependencyProperty OutlineBrushProperty = DependencyProperty.Register(nameof(OutlineBrush), typeof(Brush), typeof(TracePlot), new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Dependency property defaults have to be frozen
        /// </summary>
        private static Brush DefaultFillBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(50, 0, 160, 0));
            brush.Freeze();
            return brush;
        }

        public Brush Background
        {
            get { return (Brush)GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(TracePlot), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public string XLabel
        {
            get { return (string)GetValue(XLabelProperty); }
            set { SetValue(XLabelProperty, value); }
        }
        public static readonly DependencyProperty XLabelProperty = DependencyProperty.Register(nameof(XLabel), typeof(string), typeof(TracePlot), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string YLabel
        {
            get { return (string)GetValue(YLabelProperty); }
            set { SetValue(YLabelProperty, value); }
        }
        public static readonly DependencyProperty YLabelProperty = DependencyProperty.Register(nameof(YLabel), typeof(string), typeof(TracePlot), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Shown when there is nothing to plot
        /// </summary>
        public string EmptyText
        {
            get { return (string)GetValue(EmptyTextProperty); }
            set { SetValue(EmptyTextProperty, value); }
        }
        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(TracePlot), new FrameworkPropertyMetadata("No data", FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        private const double _marginLeft = 48;
        private const double _marginRight = 10;
        private const double _marginTop = 10;
        private const double _marginBottom = 36;
        private const double _fontSize = 11;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;
            dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
            double plotWidth = width - _marginLeft - _marginRight;
            double plotHeight = height - _marginTop - _marginBottom;
            if (plotWidth <= 0 || plotHeight <= 0)
                return;
            var plotRect = new Rect(_marginLeft, _marginTop, plotWidth, plotHeight);
            dc.DrawRectangle(null, new Pen(Brushes.Black, 1), plotRect);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var all = (Trace1 ?? Array.Empty<Point>()).Concat(Trace2 ?? Array.Empty<Point>()).ToList();
            if (all.Count == 0)
            {
                DrawText(dc, EmptyText, new Point(plotRect.Left + plotRect.Width / 2, plotRect.Top + plotRect.Height / 2), pixelsPerDip, 0.5, 0.5);
                return;
            }

            double xMin = all.Min(p => p.X), xMax = all.Max(p => p.X);
            double yMin = all.Min(p => p.Y), yMax = all.Max(p => p.Y);
            if (xMax - xMin < 1e-9)
                xMax = xMin + 1;
            Pad(ref yMin, ref yMax, 1);

            Point ToScreen(double x, double y) => new Point(
                plotRect.Left + (x - xMin) / (xMax - xMin) * plotRect.Width,
                plotRect.Bottom - (y - yMin) / (yMax - yMin) * plotRect.Height);

            DrawText(dc, xMin.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Left, plotRect.Bottom + 2), pixelsPerDip, 0, 0);
            DrawText(dc, xMax.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Right, plotRect.Bottom + 2), pixelsPerDip, 1, 0);
            DrawText(dc, XLabel, new Point(plotRect.Left + plotRect.Width / 2, plotRect.Bottom + 18), pixelsPerDip, 0.5, 0);
            DrawText(dc, yMax.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Left - 3, plotRect.Top), pixelsPerDip, 1, 0);
            DrawText(dc, yMin.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Left - 3, plotRect.Bottom), pixelsPerDip, 1, 1);
            DrawText(dc, YLabel, new Point(plotRect.Left - 3, plotRect.Top + plotRect.Height / 2), pixelsPerDip, 1, 0.5);

            dc.PushClip(new RectangleGeometry(plotRect));
            if (!double.IsNaN(FillStart) && !double.IsNaN(FillEnd) && FillEnd > FillStart)
                dc.DrawRectangle(FillBrush, null, new Rect(ToScreen(FillStart, yMax), ToScreen(FillEnd, yMin)));
            DrawTrace(dc, Trace1, Trace1Brush, ToScreen);
            DrawTrace(dc, Trace2, Trace2Brush, ToScreen);
            if (!double.IsNaN(OutlineStart) && !double.IsNaN(OutlineEnd) && OutlineEnd > OutlineStart)
            {
                var pen = new Pen(OutlineBrush, 1.5);
                dc.DrawLine(pen, ToScreen(OutlineStart, yMin), ToScreen(OutlineStart, yMax));
                dc.DrawLine(pen, ToScreen(OutlineEnd, yMin), ToScreen(OutlineEnd, yMax));
            }
            dc.Pop();
        }

        private static void DrawTrace(DrawingContext dc, IReadOnlyList<Point>? trace, Brush brush, Func<double, double, Point> toScreen)
        {
            if (trace == null || trace.Count < 2)
                return;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(toScreen(trace[0].X, trace[0].Y), false, false);
                for (int i = 1; i < trace.Count; i++)
                    ctx.LineTo(toScreen(trace[i].X, trace[i].Y), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 1.5), geometry);
        }

        /// <summary>
        /// Draws text with its anchor at relative position (ax, ay) of the text box, e.g. (0.5, 0.5) centers
        /// </summary>
        private static void DrawText(DrawingContext dc, string text, Point anchor, double pixelsPerDip, double ax, double ay)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), _fontSize, Brushes.Black, pixelsPerDip);
            dc.DrawText(ft, new Point(anchor.X - ax * ft.Width, anchor.Y - ay * ft.Height));
        }

        /// <summary>
        /// Expands a range by 5% on each side, using minSpan if the range is (close to) empty
        /// </summary>
        private static void Pad(ref double min, ref double max, double minSpan)
        {
            double span = max - min;
            if (span < minSpan)
            {
                double center = (max + min) / 2;
                min = center - minSpan / 2;
                max = center + minSpan / 2;
                span = minSpan;
            }
            min -= 0.05 * span;
            max += 0.05 * span;
        }
    }
}
