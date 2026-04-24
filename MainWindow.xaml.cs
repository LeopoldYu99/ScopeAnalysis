using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints : Window, IDisposable
    {
        private LightningChart _chart;
        private Canvas _decodeOverlay;
        private Canvas _measurementOverlay;
        private Line _measurementStartLine;
        private Line _measurementEndLine;
        private Line _measurementSpanLine;
        private System.Windows.Controls.Border _measurementValueBorder;
        private TextBlock _measurementValueText;
        private readonly Dictionary<ChartSignal, DecodeCacheEntry> _decodeCache = new Dictionary<ChartSignal, DecodeCacheEntry>();
        private readonly Dictionary<ChartSignal, MeasurementCacheEntry> _measurementCache = new Dictionary<ChartSignal, MeasurementCacheEntry>();

        private readonly List<ChartSignal> _chartSignals = new List<ChartSignal>();
        private double _lastConsumedX;
        private ProtocolImportSession _currentProtocolImportSession;
        private bool _isUpdatingImportPageSelection;

        private int _seriesCount = 4;

        private const double YMin = -0.2;
        private const double YMax = 1.2;
        private const double FixedSignalPlotHeight = 100.0;
        private const int SignalSegmentGap = 40;
        private const double ChartTopMargin = 47.0;
        private const double ChartBottomMargin = 34.0;

        private bool _isMeasurementHovering;
        private double _measurementHoverXValue;
        private ChartSignal _measurementSignal;
        private bool _isDecodeOverlayDirty = true;
        private bool _isMeasurementVisualDirty = true;
        private double _lastViewportMin = double.NaN;
        private double _lastViewportMax = double.NaN;
        private double _lastViewportWidth = double.NaN;
        private double _lastViewportHeight = double.NaN;

        private const float LineWidth = 1f;
        private const double MicrosecondsPerSecond = 1000000.0;

        public Example8BillionPoints()
        {
            InitializeComponent();
            CreateChart();
        }

        private void CreateChart()
        {
            _chart = new LightningChart();

            _chart.BeginUpdate();
            _chart.ChartName = "8 Billion Points";
            _chart.ChartRenderOptions.DeviceType = RendererDeviceType.AutoPreferD11;
            _chart.ChartRenderOptions.LineAAType2D = LineAntiAliasingType.QLAA;

            _chart.Title.Font.Size = 16;
            _chart.Title.Text = "";
            _chart.Title.Color = Color.FromArgb(255, 255, 204, 0);
            _chart.Title.Align = ChartTitleAlignment.TopCenter;

            ViewXY view = _chart.ViewXY;

            view.XAxes[0].ScrollMode = XAxisScrollMode.None;
            view.XAxes[0].SweepingGap = 0;
            view.XAxes[0].ValueType = AxisValueType.Number;
            view.XAxes[0].AutoFormatLabels = false;
            view.XAxes[0].LabelsNumberFormat = "0.000";
            view.XAxes[0].Title.Text = "";
            view.XAxes[0].AllowScrolling = true;
            view.XAxes[0].SetRange(0, 100);
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Units.Text = "us";
            view.ZoomPanOptions.DevicePrimaryButtonAction = UserInteractiveDeviceButtonAction.Pan;
            view.ZoomPanOptions.PanDirection = PanDirection.Horizontal;
            view.ZoomPanOptions.WheelZooming = WheelZooming.Off;
            view.ZoomPanOptions.AxisWheelAction = AxisWheelAction.None;

            view.DropOldSeriesData = false;

            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = SignalSegmentGap;
            view.AxisLayout.YAxisAutoPlacement = YAxisAutoPlacement.AllLeft;
            view.AxisLayout.YAxisTitleAutoPlacement = true;
            view.AxisLayout.AutoAdjustMargins = false;
            view.Margins = new Thickness(70, ChartTopMargin, 20, ChartBottomMargin);

            Band sweepBandDark = new Band(view, view.XAxes[0], view.YAxes[0])
            {
                BorderWidth = 0
            };
            sweepBandDark.Fill.Color = Color.FromArgb(255, 0, 0, 0);
            sweepBandDark.Fill.GradientColor = Color.FromArgb(0, 0, 0, 0);
            sweepBandDark.Fill.GradientFill = GradientFill.Linear;
            sweepBandDark.Fill.GradientDirection = 0;
            sweepBandDark.Binding = AxisBinding.XAxis;
            sweepBandDark.AllowUserInteraction = false;
            view.Bands.Add(sweepBandDark);

            Band sweepBandBright = new Band(view, view.XAxes[0], view.YAxes[0])
            {
                BorderWidth = 0
            };
            sweepBandBright.Fill.Color = Color.FromArgb(0, 0, 0, 0);
            sweepBandBright.Fill.GradientColor = Color.FromArgb(150, 255, 255, 255);
            sweepBandBright.Fill.GradientFill = GradientFill.Linear;
            sweepBandBright.Fill.GradientDirection = 0;
            sweepBandBright.Binding = AxisBinding.XAxis;
            sweepBandBright.AllowUserInteraction = false;
            view.Bands.Add(sweepBandBright);

            view.LegendBoxes[0].Visible = false;

            _chart.EndUpdate();
            _chart.PreviewMouseWheel += Chart_PreviewMouseWheel;
            _chart.PreviewMouseMove += Chart_PreviewMouseMove;

            _measurementOverlay = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            _decodeOverlay = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Visible
            };

            _measurementStartLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 214, 96)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _measurementEndLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 214, 96)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _measurementSpanLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(240, 255, 214, 96)),
                StrokeThickness = 1.4,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _measurementValueText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 96)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left
            };

            _measurementValueBorder = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(215, 24, 26, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 196, 64)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = _measurementValueText,
                Visibility = Visibility.Collapsed
            };

            _measurementOverlay.Children.Add(_measurementStartLine);
            _measurementOverlay.Children.Add(_measurementEndLine);
            _measurementOverlay.Children.Add(_measurementSpanLine);
            _measurementOverlay.Children.Add(_measurementValueBorder);

            gridMain.Children.Add(_chart);
            Grid.SetRow(_chart, 0);
            Grid.SetColumn(_chart, 0);

            gridMain.Children.Add(_decodeOverlay);
            Grid.SetRow(_decodeOverlay, 0);
            Grid.SetColumn(_decodeOverlay, 0);
            Panel.SetZIndex(_decodeOverlay, 1);

            gridMain.Children.Add(_measurementOverlay);
            Grid.SetRow(_measurementOverlay, 0);
            Grid.SetColumn(_measurementOverlay, 0);
            Panel.SetZIndex(_measurementOverlay, 2);

            Start();
        }

        private void UpdateImportButtons()
        {
            if (buttonImport != null)
            {
                buttonImport.Content = "Import";
                buttonImport.Visibility = Visibility.Visible;
            }

            bool hasPagedImport = _currentProtocolImportSession != null
                && _currentProtocolImportSession.Pages != null
                && _currentProtocolImportSession.Pages.Count > 0;

            if (textBlockImportPage != null)
            {
                textBlockImportPage.Visibility = hasPagedImport ? Visibility.Visible : Visibility.Collapsed;
            }

            if (comboBoxImportPage != null)
            {
                comboBoxImportPage.Visibility = hasPagedImport ? Visibility.Visible : Visibility.Collapsed;
                comboBoxImportPage.IsEnabled = hasPagedImport;
            }

            if (textBlockImportTimestamp != null)
            {
                bool hasTimestamp = hasPagedImport
                    && string.IsNullOrWhiteSpace(_currentProtocolImportSession.TimestampText) == false;
                textBlockImportTimestamp.Text = hasTimestamp
                    ? "Time: " + FormatProtocolImportTimestamp(_currentProtocolImportSession.TimestampText)
                    : string.Empty;
                textBlockImportTimestamp.Visibility = hasTimestamp ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static string FormatProtocolImportTimestamp(string timestampText)
        {
            if (string.IsNullOrWhiteSpace(timestampText))
            {
                return string.Empty;
            }

            string[] parts = timestampText.Split('_');
            if (parts.Length != 7)
            {
                return timestampText;
            }

            int year;
            int month;
            int day;
            int hour;
            int minute;
            int second;
            int millisecond;
            if (int.TryParse(parts[0], out year) == false
                || int.TryParse(parts[1], out month) == false
                || int.TryParse(parts[2], out day) == false
                || int.TryParse(parts[3], out hour) == false
                || int.TryParse(parts[4], out minute) == false
                || int.TryParse(parts[5], out second) == false
                || int.TryParse(parts[6], out millisecond) == false)
            {
                return timestampText;
            }

            return string.Format(
                "{0:0000}/{1:00}/{2:00}-{3:00}:{4:00}:{5:00}:{6:000}",
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond);
        }

        private void UpdateChartHostHeight()
        {
            if (gridMain == null)
            {
                return;
            }

            int signalCount = Math.Max(1, _seriesCount);
            double plotHeight = signalCount * FixedSignalPlotHeight;
            double gapHeight = Math.Max(0, signalCount - 1) * SignalSegmentGap;
            gridMain.Height = ChartTopMargin + plotHeight + gapHeight + ChartBottomMargin;
        }
    }
}
