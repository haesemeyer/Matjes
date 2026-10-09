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
    /// Lightweight scatter plot of up to two point series, each with an optional linear fit line
    /// </summary>
    public class FitPlot : FrameworkElement
    {
        #region Dependency properties

        public IReadOnlyList<Point>? Points1
        {
            get { return (IReadOnlyList<Point>?)GetValue(Points1Property); }
            set { SetValue(Points1Property, value); }
        }
        public static readonly DependencyProperty Points1Property = DependencyProperty.Register(nameof(Points1), typeof(IReadOnlyList<Point>), typeof(FitPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<Point>? Points2
        {
            get { return (IReadOnlyList<Point>?)GetValue(Points2Property); }
            set { SetValue(Points2Property, value); }
        }
        public static readonly DependencyProperty Points2Property = DependencyProperty.Register(nameof(Points2), typeof(IReadOnlyList<Point>), typeof(FitPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public LinearFitResult? Fit1
        {
            get { return (LinearFitResult?)GetValue(Fit1Property); }
            set { SetValue(Fit1Property, value); }
        }
        public static readonly DependencyProperty Fit1Property = DependencyProperty.Register(nameof(Fit1), typeof(LinearFitResult), typeof(FitPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public LinearFitResult? Fit2
        {
            get { return (LinearFitResult?)GetValue(Fit2Property); }
            set { SetValue(Fit2Property, value); }
        }
        public static readonly DependencyProperty Fit2Property = DependencyProperty.Register(nameof(Fit2), typeof(LinearFitResult), typeof(FitPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Series1Brush
        {
            get { return (Brush)GetValue(Series1BrushProperty); }
            set { SetValue(Series1BrushProperty, value); }
        }
        public static readonly DependencyProperty Series1BrushProperty = DependencyProperty.Register(nameof(Series1Brush), typeof(Brush), typeof(FitPlot), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Series2Brush
        {
            get { return (Brush)GetValue(Series2BrushProperty); }
            set { SetValue(Series2BrushProperty, value); }
        }
        public static readonly DependencyProperty Series2BrushProperty = DependencyProperty.Register(nameof(Series2Brush), typeof(Brush), typeof(FitPlot), new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Background
        {
            get { return (Brush)GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(FitPlot), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public string XLabel
        {
            get { return (string)GetValue(XLabelProperty); }
            set { SetValue(XLabelProperty, value); }
        }
        public static readonly DependencyProperty XLabelProperty = DependencyProperty.Register(nameof(XLabel), typeof(string), typeof(FitPlot), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public string YLabel
        {
            get { return (string)GetValue(YLabelProperty); }
            set { SetValue(YLabelProperty, value); }
        }
        public static readonly DependencyProperty YLabelProperty = DependencyProperty.Register(nameof(YLabel), typeof(string), typeof(FitPlot), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        private const double _marginLeft = 48;
        private const double _marginRight = 10;
        private const double _marginTop = 10;
        private const double _marginBottom = 36;
        private const double _markerRadius = 4;
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
            var axisPen = new Pen(Brushes.Black, 1);
            dc.DrawRectangle(null, axisPen, plotRect);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var all = (Points1 ?? Array.Empty<Point>()).Concat(Points2 ?? Array.Empty<Point>()).ToList();
            if (all.Count == 0)
            {
                DrawText(dc, "No calibration points", new Point(plotRect.Left + plotRect.Width / 2, plotRect.Top + plotRect.Height / 2), pixelsPerDip, 0.5, 0.5);
                return;
            }

            // Determine data limits with some padding, guarding against zero ranges
            double xMin = all.Min(p => p.X), xMax = all.Max(p => p.X);
            double yMin = all.Min(p => p.Y), yMax = all.Max(p => p.Y);
            Pad(ref xMin, ref xMax, 10);
            Pad(ref yMin, ref yMax, 0.1);

            Point ToScreen(double x, double y) => new Point(
                plotRect.Left + (x - xMin) / (xMax - xMin) * plotRect.Width,
                plotRect.Bottom - (y - yMin) / (yMax - yMin) * plotRect.Height);

            // Axis limit labels
            DrawText(dc, xMin.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Left, plotRect.Bottom + 2), pixelsPerDip, 0, 0);
            DrawText(dc, xMax.ToString("F0", CultureInfo.InvariantCulture), new Point(plotRect.Right, plotRect.Bottom + 2), pixelsPerDip, 1, 0);
            DrawText(dc, XLabel, new Point(plotRect.Left + plotRect.Width / 2, plotRect.Bottom + 18), pixelsPerDip, 0.5, 0);
            DrawText(dc, yMax.ToString("F2", CultureInfo.InvariantCulture), new Point(plotRect.Left - 3, plotRect.Top), pixelsPerDip, 1, 0);
            DrawText(dc, yMin.ToString("F2", CultureInfo.InvariantCulture), new Point(plotRect.Left - 3, plotRect.Bottom), pixelsPerDip, 1, 1);
            DrawText(dc, YLabel, new Point(plotRect.Left - 3, plotRect.Top + plotRect.Height / 2), pixelsPerDip, 1, 0.5);

            dc.PushClip(new RectangleGeometry(plotRect));
            DrawSeries(dc, Points1, Fit1, Series1Brush, xMin, xMax, ToScreen);
            DrawSeries(dc, Points2, Fit2, Series2Brush, xMin, xMax, ToScreen);
            dc.Pop();
        }

        private static void DrawSeries(DrawingContext dc, IReadOnlyList<Point>? points, LinearFitResult? fit, Brush brush, double xMin, double xMax, Func<double, double, Point> toScreen)
        {
            if (fit != null)
                dc.DrawLine(new Pen(brush, 1.5), toScreen(xMin, fit.Predict(xMin)), toScreen(xMax, fit.Predict(xMax)));
            if (points == null)
                return;
            foreach (var p in points)
                dc.DrawEllipse(brush, new Pen(Brushes.Black, 0.5), toScreen(p.X, p.Y), _markerRadius, _markerRadius);
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
        /// Expands a range by 10% on each side, using minSpan if the range is (close to) empty
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
            min -= 0.1 * span;
            max += 0.1 * span;
        }
    }
}
