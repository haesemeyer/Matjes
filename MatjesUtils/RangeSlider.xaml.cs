using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MatjesUtils
{
    /// <summary>
    /// A dual-thumb range slider. The overall selectable bounds are set via
    /// <see cref="Minimum"/> / <see cref="Maximum"/>; the user-selected sub-range
    /// is exposed via <see cref="LowerValue"/> / <see cref="UpperValue"/>.
    /// Set <see cref="IsVertical"/> to lay the slider out top-to-bottom instead
    /// of left-to-right (in that case, give the control an explicit Height).
    /// </summary>
    public partial class RangeSlider : UserControl
    {
        // Thickness of the control along its "cross" axis (e.g. control height when horizontal).
        private const double SliderThickness = 20;
        // Thickness of the visible track bar itself.
        private const double TrackThickness = 4;
        // Thumb diameter (must match the RangeThumbStyle Width/Height in XAML).
        private const double ThumbSize = 16;

        public RangeSlider()
        {
            InitializeComponent();
            ApplyOrientation();
            Loaded += (s, e) =>
            {
                UpdateThumbPositions();
                UpdateRangeText();
            };
        }

        #region Dependency Properties

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum), typeof(double), typeof(RangeSlider),
                new FrameworkPropertyMetadata(0d, OnRangeLimitsChanged, CoerceMinimum));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum), typeof(double), typeof(RangeSlider),
                new FrameworkPropertyMetadata(100d, OnRangeLimitsChanged, CoerceMaximum));

        public static readonly DependencyProperty LowerValueProperty =
            DependencyProperty.Register(
                nameof(LowerValue), typeof(double), typeof(RangeSlider),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnLowerValueChanged, CoerceLowerValue));

        public static readonly DependencyProperty UpperValueProperty =
            DependencyProperty.Register(
                nameof(UpperValue), typeof(double), typeof(RangeSlider),
                new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnUpperValueChanged, CoerceUpperValue));

        /// <summary>Smallest permissible gap between LowerValue and UpperValue.</summary>
        public static readonly DependencyProperty MinimumRangeProperty =
            DependencyProperty.Register(
                nameof(MinimumRange), typeof(double), typeof(RangeSlider),
                new FrameworkPropertyMetadata(0d, OnRangeLimitsChanged));

        /// <summary>Whether the "min - max" text readout is shown below the slider.</summary>
        public static readonly DependencyProperty ShowRangeTextProperty =
            DependencyProperty.Register(
                nameof(ShowRangeText), typeof(bool), typeof(RangeSlider),
                new FrameworkPropertyMetadata(false));

        /// <summary>Composite-format string used for the readout, e.g. "{0:0.##} - {1:0.##}".</summary>
        public static readonly DependencyProperty RangeTextFormatProperty =
            DependencyProperty.Register(
                nameof(RangeTextFormat), typeof(string), typeof(RangeSlider),
                new FrameworkPropertyMetadata("{0:0.##} - {1:0.##}", OnRangeTextFormatChanged));

        private static readonly DependencyPropertyKey RangeTextPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(RangeText), typeof(string), typeof(RangeSlider),
                new FrameworkPropertyMetadata(string.Empty));

        public static readonly DependencyProperty RangeTextProperty = RangeTextPropertyKey.DependencyProperty;

        /// <summary>When true, the slider is laid out vertically (bottom = Minimum, top = Maximum)
        /// instead of horizontally (left = Minimum, right = Maximum).</summary>
        public static readonly DependencyProperty IsVerticalProperty =
            DependencyProperty.Register(
                nameof(IsVertical), typeof(bool), typeof(RangeSlider),
                new FrameworkPropertyMetadata(false, OnIsVerticalChanged));

        /// <summary>Value the lower handle snaps to when double-clicked. Null (the default)
        /// disables double-click-to-reset for that handle.</summary>
        public static readonly DependencyProperty DefaultLowerValueProperty =
            DependencyProperty.Register(
                nameof(DefaultLowerValue), typeof(double?), typeof(RangeSlider),
                new FrameworkPropertyMetadata((double?)null));

        /// <summary>Value the upper handle snaps to when double-clicked. Null (the default)
        /// disables double-click-to-reset for that handle.</summary>
        public static readonly DependencyProperty DefaultUpperValueProperty =
            DependencyProperty.Register(
                nameof(DefaultUpperValue), typeof(double?), typeof(RangeSlider),
                new FrameworkPropertyMetadata((double?)null));

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double LowerValue
        {
            get => (double)GetValue(LowerValueProperty);
            set => SetValue(LowerValueProperty, value);
        }

        public double UpperValue
        {
            get => (double)GetValue(UpperValueProperty);
            set => SetValue(UpperValueProperty, value);
        }

        public double MinimumRange
        {
            get => (double)GetValue(MinimumRangeProperty);
            set => SetValue(MinimumRangeProperty, value);
        }

        public bool ShowRangeText
        {
            get => (bool)GetValue(ShowRangeTextProperty);
            set => SetValue(ShowRangeTextProperty, value);
        }

        public string RangeTextFormat
        {
            get => (string)GetValue(RangeTextFormatProperty);
            set => SetValue(RangeTextFormatProperty, value);
        }

        public string RangeText
        {
            get => (string)GetValue(RangeTextProperty);
            private set => SetValue(RangeTextPropertyKey, value);
        }

        public bool IsVertical
        {
            get => (bool)GetValue(IsVerticalProperty);
            set => SetValue(IsVerticalProperty, value);
        }

        public double? DefaultLowerValue
        {
            get => (double?)GetValue(DefaultLowerValueProperty);
            set => SetValue(DefaultLowerValueProperty, value);
        }

        public double? DefaultUpperValue
        {
            get => (double?)GetValue(DefaultUpperValueProperty);
            set => SetValue(DefaultUpperValueProperty, value);
        }

        #endregion

        #region Change notifications for consumers

        public static readonly RoutedEvent LowerValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(LowerValueChanged), RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeSlider));

        public event RoutedPropertyChangedEventHandler<double> LowerValueChanged
        {
            add => AddHandler(LowerValueChangedEvent, value);
            remove => RemoveHandler(LowerValueChangedEvent, value);
        }

        public static readonly RoutedEvent UpperValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(UpperValueChanged), RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeSlider));

        public event RoutedPropertyChangedEventHandler<double> UpperValueChanged
        {
            add => AddHandler(UpperValueChangedEvent, value);
            remove => RemoveHandler(UpperValueChangedEvent, value);
        }

        #endregion

        #region Coercion / property-changed callbacks

        private static object CoerceMinimum(DependencyObject d, object value)
        {
            var rs = (RangeSlider)d;
            double min = (double)value;
            return min > rs.Maximum ? rs.Maximum : min;
        }

        private static object CoerceMaximum(DependencyObject d, object value)
        {
            var rs = (RangeSlider)d;
            double max = (double)value;
            return max < rs.Minimum ? rs.Minimum : max;
        }

        private static void OnRangeLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rs = (RangeSlider)d;
            rs.CoerceValue(LowerValueProperty);
            rs.CoerceValue(UpperValueProperty);
            rs.UpdateThumbPositions();
        }

        private static object CoerceLowerValue(DependencyObject d, object value)
        {
            var rs = (RangeSlider)d;
            double v = Clamp((double)value, rs.Minimum, rs.Maximum);
            double maxAllowed = rs.UpperValue - rs.MinimumRange;
            if (v > maxAllowed) v = Math.Max(rs.Minimum, maxAllowed);
            return v;
        }

        private static object CoerceUpperValue(DependencyObject d, object value)
        {
            var rs = (RangeSlider)d;
            double v = Clamp((double)value, rs.Minimum, rs.Maximum);
            double minAllowed = rs.LowerValue + rs.MinimumRange;
            if (v < minAllowed) v = Math.Min(rs.Maximum, minAllowed);
            return v;
        }

        private static void OnLowerValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rs = (RangeSlider)d;
            rs.CoerceValue(UpperValueProperty);
            rs.UpdateThumbPositions();
            rs.UpdateRangeText();
            rs.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
                (double)e.OldValue, (double)e.NewValue, LowerValueChangedEvent));
        }

        private static void OnUpperValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rs = (RangeSlider)d;
            rs.CoerceValue(LowerValueProperty);
            rs.UpdateThumbPositions();
            rs.UpdateRangeText();
            rs.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
                (double)e.OldValue, (double)e.NewValue, UpperValueChangedEvent));
        }

        private static void OnRangeTextFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((RangeSlider)d).UpdateRangeText();
        }

        private static void OnIsVerticalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var rs = (RangeSlider)d;
            rs.ApplyOrientation();
            rs.UpdateThumbPositions();
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        #endregion

        #region Orientation layout

        // Switches the track/thumb sizing and alignment between horizontal and vertical layout.
        // Called once at construction and again whenever IsVertical changes.
        // Note: cross-axis offsets (centring the track bar and thumbs) are NOT set here --
        // they depend on the canvas's measured size and are applied in UpdateThumbPositions.
        private void ApplyOrientation()
        {
            if (PART_SliderGrid == null) return;

            if (IsVertical)
            {
                // Let the slider row fill all space the control is given, minus the text
                // row below it. Without this the row (and the Canvas inside it) stays at
                // its Auto size, which is 0 for empty content, and both thumbs collapse
                // onto the same point.
                PART_SliderRow.Height = new GridLength(1, GridUnitType.Star);

                PART_SliderGrid.Width = SliderThickness;
                PART_SliderGrid.Height = double.NaN;
                PART_SliderGrid.HorizontalAlignment = HorizontalAlignment.Left;
                PART_SliderGrid.VerticalAlignment = VerticalAlignment.Stretch;
                PART_SliderGrid.Margin = new Thickness(4, 8, 4, 8);

                PART_TrackBackground.Width = TrackThickness;
                PART_TrackBackground.Height = double.NaN;
                PART_TrackBackground.HorizontalAlignment = HorizontalAlignment.Center;
                PART_TrackBackground.VerticalAlignment = VerticalAlignment.Stretch;

                PART_SelectedRange.Width = TrackThickness;
                PART_SelectedRange.Height = double.NaN;
            }
            else
            {
                // Auto row + explicit grid height, so the grid's Margin is added around the
                // 20px band rather than being subtracted from it. (A fixed 20px row left the
                // canvas only 12px tall, which pushed the track bar, the range bar and the
                // thumbs to three different heights.)
                PART_SliderRow.Height = GridLength.Auto;

                PART_SliderGrid.Height = SliderThickness;
                PART_SliderGrid.Width = double.NaN;
                PART_SliderGrid.VerticalAlignment = VerticalAlignment.Top;
                PART_SliderGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                PART_SliderGrid.Margin = new Thickness(8, 4, 8, 4);

                PART_TrackBackground.Height = TrackThickness;
                PART_TrackBackground.Width = double.NaN;
                PART_TrackBackground.VerticalAlignment = VerticalAlignment.Center;
                PART_TrackBackground.HorizontalAlignment = HorizontalAlignment.Stretch;

                PART_SelectedRange.Height = TrackThickness;
                PART_SelectedRange.Width = double.NaN;
            }
        }

        #endregion

        #region Layout and drag handling

        // Usable travel distance along the slider's main axis, in pixels.
        private double TrackLength =>
            PART_TrackCanvas == null
                ? 0
                : IsVertical
                    ? Math.Max(0, PART_TrackCanvas.ActualHeight - ThumbSize)
                    : Math.Max(0, PART_TrackCanvas.ActualWidth - ThumbSize);

        // Converts a value to a Canvas.Left (horizontal) / Canvas.Top (vertical) coordinate.
        // Vertical is inverted so the top of the track represents Maximum, matching the
        // convention used by WPF's own vertical Slider.
        private double ValueToPosition(double value)
        {
            double range = Maximum - Minimum;
            if (range <= 0) return 0;
            double ratio = (value - Minimum) / range;
            double pos = ratio * TrackLength;
            return IsVertical ? TrackLength - pos : pos;
        }

        private double PositionToValue(double pos)
        {
            double length = TrackLength;
            if (length <= 0) return Minimum;
            pos = Clamp(pos, 0, length);
            double ratio = IsVertical ? (length - pos) / length : pos / length;
            return Minimum + ratio * (Maximum - Minimum);
        }

        private void UpdateThumbPositions()
        {
            if (PART_TrackCanvas == null || PART_MinThumb == null || PART_MaxThumb == null || PART_SelectedRange == null)
                return;

            UpdateCrossAxisPositions();

            double posLower = ValueToPosition(LowerValue);
            double posUpper = ValueToPosition(UpperValue);

            if (IsVertical)
            {
                Canvas.SetTop(PART_MinThumb, posLower);
                Canvas.SetTop(PART_MaxThumb, posUpper);
            }
            else
            {
                Canvas.SetLeft(PART_MinThumb, posLower);
                Canvas.SetLeft(PART_MaxThumb, posUpper);
            }

            double thumbHalf = ThumbSize / 2.0;
            double rangeStart = Math.Min(posLower, posUpper) + thumbHalf;
            double rangeLength = Math.Max(0, Math.Abs(posUpper - posLower));

            if (IsVertical)
            {
                Canvas.SetTop(PART_SelectedRange, rangeStart);
                PART_SelectedRange.Height = rangeLength;
            }
            else
            {
                Canvas.SetLeft(PART_SelectedRange, rangeStart);
                PART_SelectedRange.Width = rangeLength;
            }
        }

        // Centres the thumbs and the selected-range bar on the track's cross axis, using the
        // canvas's *measured* size rather than a constant. Keeping this size-driven means the
        // thumbs, the track bar and the range bar can never drift apart if margins, row sizing
        // or SliderThickness change.
        private void UpdateCrossAxisPositions()
        {
            double cross = IsVertical ? PART_TrackCanvas.ActualWidth : PART_TrackCanvas.ActualHeight;
            if (cross <= 0) cross = SliderThickness; // before the first layout pass

            double thumbOffset = (cross - ThumbSize) / 2.0;
            double barOffset = (cross - TrackThickness) / 2.0;

            if (IsVertical)
            {
                Canvas.SetLeft(PART_MinThumb, thumbOffset);
                Canvas.SetLeft(PART_MaxThumb, thumbOffset);
                Canvas.SetLeft(PART_SelectedRange, barOffset);
            }
            else
            {
                Canvas.SetTop(PART_MinThumb, thumbOffset);
                Canvas.SetTop(PART_MaxThumb, thumbOffset);
                Canvas.SetTop(PART_SelectedRange, barOffset);
            }
        }

        private void UpdateRangeText()
        {
            if (PART_RangeText == null) return;
            try
            {
                RangeText = string.Format(RangeTextFormat ?? "{0} - {1}", LowerValue, UpperValue);
            }
            catch (FormatException)
            {
                RangeText = $"{LowerValue} - {UpperValue}";
            }
        }

        private void PART_TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThumbPositions();
        }

        private void PART_MinThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double delta = IsVertical ? e.VerticalChange : e.HorizontalChange;
            double newPos = ValueToPosition(LowerValue) + delta;
            LowerValue = PositionToValue(newPos); // coercion keeps it <= UpperValue - MinimumRange
        }

        private void PART_MaxThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double delta = IsVertical ? e.VerticalChange : e.HorizontalChange;
            double newPos = ValueToPosition(UpperValue) + delta;
            UpperValue = PositionToValue(newPos); // coercion keeps it >= LowerValue + MinimumRange
        }

        // Double-clicking a handle snaps it back to its configured default, if any.
        // This must hook the TUNNELING preview event: Thumb's own class handler for the
        // bubbling MouseLeftButtonDown starts the drag and marks the event Handled, and
        // class handlers run before XAML-attached instance handlers, so a bubbling
        // handler here would never be invoked.
        // Assigning through the LowerValue/UpperValue properties still runs the usual
        // coercion, so the default is clamped to the valid range / MinimumRange gap
        // just like any other value.
        private void PART_MinThumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && DefaultLowerValue.HasValue)
            {
                LowerValue = DefaultLowerValue.Value;
                e.Handled = true; // also stops the Thumb starting a drag on the second click
            }
        }

        private void PART_MaxThumb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && DefaultUpperValue.HasValue)
            {
                UpperValue = DefaultUpperValue.Value;
                e.Handled = true;
            }
        }

        // Clicking on the track (not on a thumb) snaps the nearer thumb to that position.
        private void PART_TrackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src && FindThumbAncestor(src) != null) return;

            Point pos = e.GetPosition(PART_TrackCanvas);
            double clickPos = (IsVertical ? pos.Y : pos.X) - ThumbSize / 2.0;
            double clickValue = PositionToValue(clickPos);

            if (Math.Abs(clickValue - LowerValue) <= Math.Abs(clickValue - UpperValue))
                LowerValue = clickValue;
            else
                UpperValue = clickValue;
        }

        private static Thumb FindThumbAncestor(DependencyObject d)
        {
            while (d != null)
            {
                if (d is Thumb t) return t;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        #endregion
    }
}
