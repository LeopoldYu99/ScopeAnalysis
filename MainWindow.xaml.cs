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

namespace InteractiveExamples
{
    public partial class Example8BillionPoints : Window, IDisposable
    {
        private LightningChart _chart;

        private Timer _producerTimer;
        private readonly SignalProducer _signalProducer = new SignalProducer();

        private readonly List<ChartSignal> _chartSignals = new List<ChartSignal>();
        private double _lastConsumedX;
        private bool _isStreaming;

        private int _seriesCount;
        private int _appendCountPerRound = DefaultAppendCountPerRound;
        private double _xLen;

        private const double YMin = 0;
        private const double YMax = 100;

        private bool _hasConsumedData;

        private const float LineWidth = 1f;
        private const int ProducerIntervalMs = 50;
        private const int DefaultAppendCountPerRound = 10;

        private enum XAxisViewMode
        {
            FollowScroll,
            Paging,
            Free
        }

        private XAxisViewMode _xAxisViewMode = XAxisViewMode.Paging;

        public Example8BillionPoints()
        {
            InitializeComponent();
            _producerTimer = new Timer(ProducerTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            textBoxAppendCountPerRound.Text = DefaultAppendCountPerRound.ToString();
            UpdateXAxisViewModeButtons();
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
            view.XAxes[0].LabelsNumberFormat = "0.000";
            view.XAxes[0].Title.Text = "Time";
            view.XAxes[0].SetRange(0, 100000);
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Units.Text = "s";

            view.DropOldSeriesData = true;

            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = 10;
            view.AxisLayout.YAxisAutoPlacement = YAxisAutoPlacement.AllLeft;
            view.AxisLayout.YAxisTitleAutoPlacement = true;
            view.AxisLayout.AutoAdjustMargins = false;
            view.Margins = new Thickness(70, 17, 20, 34);

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

            gridMain.Children.Add(_chart);
            Grid.SetRow(_chart, 0);
            Grid.SetColumn(_chart, 0);
            Start();
        }
    }
}
