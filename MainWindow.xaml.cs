using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using System;
using System.Collections.Generic;
using System.Threading;
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
        private Canvas _cursorOverlay;
        private Line _cursorLine;
        private Line _cursorMeasurementStartLine;
        private Line _cursorMeasurementEndLine;
        private Line _cursorMeasurementSpanLine;
        private System.Windows.Controls.Border _cursorValueBorder;
        private TextBlock _cursorValueText;
        private readonly List<System.Windows.Controls.Border> _cursorAxisValueBorders = new List<System.Windows.Controls.Border>();
        private readonly List<TextBlock> _cursorAxisValueTexts = new List<TextBlock>();
        private readonly Dictionary<ChartSignal, DecodeCacheEntry> _decodeCache = new Dictionary<ChartSignal, DecodeCacheEntry>();

        private Timer _producerTimer;
        private readonly SignalProducer _signalProducer = new SignalProducer();
        private const bool UseBinaryFileDataSource = true;
        private const string BinaryWaveFilePath = @"C:\Users\admin-250327\Desktop\net9.0-windows\serial_wave.bin";

        private readonly List<ChartSignal> _chartSignals = new List<ChartSignal>();
        private double _lastConsumedX;
        private bool _isStreaming;

        private int _seriesCount = 4;
        private int _appendCountPerRound = DefaultAppendCountPerRound;
        private double _xLen = 200;

        private const double YMin = 0;
        private const double YMax = 1.2;

        private bool _hasConsumedData;
        private bool _isCursorEnabled;
        private bool _isCursorHovering;
        private bool _isCursorDragging;
        private double _cursorXValue;
        private double _hoverMeasurementXValue;
        private ChartSignal _cursorMeasurementSignal;
        private bool _arePointsVisible;
        private bool _isDecodeVisible = true;
        private bool _isDecodeOverlayDirty = true;
        private bool _isCursorVisualDirty = true;
        private double _lastViewportMin = double.NaN;
        private double _lastViewportMax = double.NaN;
        private double _lastViewportWidth = double.NaN;
        private double _lastViewportHeight = double.NaN;

        private const float LineWidth = 1f;
        private const int ProducerIntervalMs = 50;
        private const int DefaultAppendCountPerRound = 10;
        private const double MicrosecondsPerSecond = 1000000.0;
        private const double ImportedSampleRate = 1000000.0;

        public Example8BillionPoints()
        {
            InitializeComponent();
            _producerTimer = new Timer(ProducerTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            UpdateCursorButton();
            UpdatePointsButton();
            UpdateDecodeButton();
            CreateChart();
        }

        private double CurrentSampleIntervalSeconds
        {
            get
            {
                return ProducerIntervalMs / 1000.0 / Math.Max(1, _appendCountPerRound);
            }
        }

        private double VisibleRangeSeconds
        {
            get
            {
                return _xLen * CurrentSampleIntervalSeconds;
            }
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

            var view = _chart.ViewXY;

            view.XAxes[0].ScrollMode = XAxisScrollMode.Scrolling;
            view.XAxes[0].SweepingGap = 0;
            view.XAxes[0].ValueType = AxisValueType.Number;
            view.XAxes[0].AutoFormatLabels = false;
            view.XAxes[0].LabelsNumberFormat = UseBinaryFileDataSource ? "0" : "0.000";
            view.XAxes[0].Title.Text = "";
            view.XAxes[0].AllowScrolling = true;
            view.XAxes[0].SetRange(0, 100000);
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Units.Text = UseBinaryFileDataSource ? "us" : "s";
            view.ZoomPanOptions.DevicePrimaryButtonAction = UserInteractiveDeviceButtonAction.Pan;
            view.ZoomPanOptions.PanDirection = PanDirection.Horizontal;
            view.ZoomPanOptions.WheelZooming = WheelZooming.Off;
            view.ZoomPanOptions.AxisWheelAction = AxisWheelAction.None;

            view.DropOldSeriesData = true;

            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = 40;
            view.AxisLayout.YAxisAutoPlacement = YAxisAutoPlacement.AllLeft;
            view.AxisLayout.YAxisTitleAutoPlacement = true;
            view.AxisLayout.AutoAdjustMargins = false;
            view.Margins = new Thickness(70, 47, 20, 34);

            var sweepBandDark = new Band(view, view.XAxes[0], view.YAxes[0])
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

            var sweepBandBright = new Band(view, view.XAxes[0], view.YAxes[0])
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
            _chart.PreviewMouseLeftButtonDown += Chart_PreviewMouseLeftButtonDown;
            _chart.PreviewMouseMove += Chart_PreviewMouseMove;
            _chart.PreviewMouseLeftButtonUp += Chart_PreviewMouseLeftButtonUp;
            _chart.MouseLeave += Chart_MouseLeave;
            _chart.LostMouseCapture += Chart_LostMouseCapture;

            _cursorOverlay = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            _decodeOverlay = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = _isDecodeVisible ? Visibility.Visible : Visibility.Collapsed
            };

            _cursorLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(190, 255, 196, 64)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new[] { 4.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _cursorMeasurementStartLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 214, 96)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _cursorMeasurementEndLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 214, 96)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new[] { 3.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _cursorMeasurementSpanLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(240, 255, 214, 96)),
                StrokeThickness = 1.4,
                StrokeDashArray = new DoubleCollection(new[] { 5.0, 3.0 }),
                SnapsToDevicePixels = true,
                Visibility = Visibility.Collapsed
            };

            _cursorValueText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 96)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left
            };

            _cursorValueBorder = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(215, 24, 26, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 196, 64)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = _cursorValueText,
                Visibility = Visibility.Collapsed
            };

            _cursorOverlay.Children.Add(_cursorLine);
            _cursorOverlay.Children.Add(_cursorMeasurementStartLine);
            _cursorOverlay.Children.Add(_cursorMeasurementEndLine);
            _cursorOverlay.Children.Add(_cursorMeasurementSpanLine);
            _cursorOverlay.Children.Add(_cursorValueBorder);

            gridMain.Children.Add(_chart);
            Grid.SetRow(_chart, 0);
            Grid.SetColumn(_chart, 0);

            gridMain.Children.Add(_decodeOverlay);
            Grid.SetRow(_decodeOverlay, 0);
            Grid.SetColumn(_decodeOverlay, 0);
            Panel.SetZIndex(_decodeOverlay, 1);

            gridMain.Children.Add(_cursorOverlay);
            Grid.SetRow(_cursorOverlay, 0);
            Grid.SetColumn(_cursorOverlay, 0);
            Panel.SetZIndex(_cursorOverlay, 2);

            Start();
        }
    }
}
