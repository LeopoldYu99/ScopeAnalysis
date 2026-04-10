// ------------------------------------------------------------------------------------------------------
// LightningChart® example code: Demo shows a record volume of data, over 1 Billion points in real-time. 
//
// If you need any assistance, or notice error in this example code, please contact support@lightningchart.com. 
//
// Permission to use this code in your application comes with LightningChart® license. 
//
// https://www.arction.com | support@lightningchart.com | sales@lightningchart.com
//
// © Arction Ltd 2009-2021. All rights reserved.  
// ------------------------------------------------------------------------------------------------------
using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints : Window, IDisposable
    {
        //The chart
        private LightningChart _chart;


        //Background producer timer
        private Timer _producerTimer;
        private readonly SignalProducer _signalProducer = new SignalProducer();

        //Producer / consumer queue
        private readonly List<ChartSignal> _chartSignals = new List<ChartSignal>();
        private double _lastConsumedX;
        private bool _isStreaming;

        //Series count 
        private int _seriesCount;

        //Append data point per round count
        private int _appendCountPerRound = DefaultAppendCountPerRound;

        //X axis length 
        private double _xLen;

        //Y axis minimum 
        private const double YMin = 0;

        //Y axis maximum 
        private const double YMax = 100;

        //Points appended thus far 
        private long _pointsAppended;

        //Line width in pixels 
        private const float LineWidth = 1f;
        private const int ProducerIntervalMs = 50;
        private const int DefaultAppendCountPerRound = 10;
        private const int MaxRoundsPerRender = 4;

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

        private void updatefps(object sender, EventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            TotalDataPoints.Content = "Total data points: " + (_pointsAppended * _seriesCount).ToString("N0");
        }

        private void CreateChart()
        {
            _chart = new LightningChart();

            _chart.BeginUpdate();
            _chart.ChartName = "8 Billion Points";
            _chart.ChartRenderOptions.DeviceType = RendererDeviceType.AutoPreferD11;
            _chart.ChartRenderOptions.LineAAType2D = LineAntiAliasingType.QLAA;

            _chart.Title.Font.Size = 16;
            _chart.Title.Text = "";//"Set options and press Start.\nPC with 16GB RAM + fast graphics card is strongly recommended";
            _chart.Title.Color = Color.FromArgb(255, 255, 204, 0);
            _chart.Title.Align = ChartTitleAlignment.TopCenter;

            ViewXY view = _chart.ViewXY;

            //Set real-time monitoring scroll mode 
            view.XAxes[0].ScrollMode = XAxisScrollMode.Scrolling;
            view.XAxes[0].SweepingGap = 0;
            view.XAxes[0].ValueType = AxisValueType.Number;
            view.XAxes[0].AutoFormatLabels = false;
            view.XAxes[0].LabelsNumberFormat = "0.000";
            view.XAxes[0].Title.Text = "Time";
            view.XAxes[0].SetRange(0, 100000);
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Units.Text = "s";

            //Set real-time monitoring automatic old data destruction
            view.DropOldSeriesData = true;

            //Set Axis layout to Segmented
            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = 10;
            view.AxisLayout.YAxisAutoPlacement = YAxisAutoPlacement.AllLeft;
            view.AxisLayout.YAxisTitleAutoPlacement = true;

            // fix margins to prevent Graph resize, which may take long time for Billion points
            view.AxisLayout.AutoAdjustMargins = false;
            view.Margins = new Thickness(70, 17, 20, 34);


            //Create a dark sweeping gradient band for old page
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

            //Create a bright sweeping gradient band, for new page
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

            //Don't show legend box
            view.LegendBoxes[0].Visible = false;

            _chart.EndUpdate();
            _chart.PreviewMouseWheel += Chart_PreviewMouseWheel;

            gridMain.Children.Add(_chart);
            Grid.SetRow(_chart, 0);
            Grid.SetColumn(_chart, 0);
            Start();
        }

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            ConsumePendingSignalRounds(MaxRoundsPerRender);
            FpsCounter.Content = string.Format("Queue: {0} rounds / {1:N0} points", _signalProducer.PendingRoundCount, _signalProducer.PendingSampleCount);
            TotalDataPoints.Content = "Total data points: " + (_pointsAppended * _seriesCount).ToString("N0");
            long visibleSampleCount = Math.Min((long)Math.Ceiling((_chart.ViewXY.XAxes[0].Maximum - _chart.ViewXY.XAxes[0].Minimum) / CurrentSampleIntervalSeconds), _pointsAppended);
            DataPointsInVisibleArea.Content = "Visible data points: " + (visibleSampleCount * _seriesCount).ToString("N0");
        }

        private void Chart_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_chart == null)
            {
                return;
            }

            Point position = e.GetPosition(_chart);
            Thickness margins = _chart.ViewXY.Margins;
            double zoomFactor = e.Delta > 0 ? 0.8 : 1.25;

            if (position.X <= margins.Left)
            {
                ZoomY(zoomFactor);
                e.Handled = true;
                return;
            }

            if (position.Y >= _chart.ActualHeight - margins.Bottom)
            {
                double? anchorX = TryGetXAxisValueAt(position.X);
                ZoomX(zoomFactor, anchorX);
                e.Handled = true;
            }
        }

        private double? TryGetXAxisValueAt(double controlX)
        {
            if (_chart == null)
            {
                return null;
            }

            AxisX xAxis = _chart.ViewXY.XAxes[0];
            Thickness margins = _chart.ViewXY.Margins;
            double plotWidth = _chart.ActualWidth - margins.Left - margins.Right;
            if (plotWidth <= 0)
            {
                return null;
            }

            double normalizedX = (controlX - margins.Left) / plotWidth;
            normalizedX = Math.Max(0, Math.Min(1, normalizedX));
            return xAxis.Minimum + (xAxis.Maximum - xAxis.Minimum) * normalizedX;
        }

        private void SetXAxisViewMode(XAxisViewMode mode)
        {
            _xAxisViewMode = mode;
            UpdateXAxisViewModeButtons();

            if (_chart == null || _pointsAppended <= 0 || mode != XAxisViewMode.FollowScroll)
            {
                return;
            }

            _chart.BeginUpdate();
            UpdateXAxisView(_lastConsumedX);
            _chart.EndUpdate();
        }

        private void UpdateXAxisViewModeButtons()
        {
            UpdateXAxisViewModeButton(buttonFollowScrollMode, _xAxisViewMode == XAxisViewMode.FollowScroll);
            UpdateXAxisViewModeButton(buttonPageMode, _xAxisViewMode == XAxisViewMode.Paging);
            UpdateXAxisViewModeButton(buttonFreeMode, _xAxisViewMode == XAxisViewMode.Free);
        }

        private static void UpdateXAxisViewModeButton(Button button, bool isActive)
        {
            if (button == null)
            {
                return;
            }

            button.Background = isActive ? new SolidColorBrush(Color.FromRgb(255, 204, 0)) : new SolidColorBrush(Color.FromRgb(70, 70, 70));
            button.BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(255, 230, 140)) : new SolidColorBrush(Color.FromRgb(120, 120, 120));
            button.Foreground = isActive ? Brushes.Black : Brushes.White;
        }

        private void UpdateXAxisView(double lastX)
        {
            AxisX xAxis = _chart.ViewXY.XAxes[0];

            if (_xAxisViewMode == XAxisViewMode.FollowScroll)
            {
                xAxis.ScrollPosition = lastX;
                return;
            }

            if (_xAxisViewMode != XAxisViewMode.Paging || lastX < xAxis.Maximum)
            {
                return;
            }

            double pageWidth = xAxis.Maximum - xAxis.Minimum;
            if (pageWidth <= 0)
            {
                pageWidth = _xLen;
            }

            xAxis.SetRange(lastX, lastX + pageWidth);
        }

        private void buttonStartStop_Click(object sender, RoutedEventArgs e)
        {
            Start();
        }


        public void Start()
        {
            Stop();

            _pointsAppended = 0;
            _lastConsumedX = 0;
            buttonStartStop.Content = "Restart";

            ViewXY v = _chart.ViewXY;

            //Read series count
            try
            {
                _seriesCount = int.Parse(textBoxSeriesCount.Text);
            }
            catch
            {
                MessageBox.Show("Invalid series count text input");
                return;
            }

            //Read append count / round
            try
            {
                _appendCountPerRound = int.Parse(textBoxAppendCountPerRound.Text);
            }
            catch
            {
                MessageBox.Show("Invalid append count");
                return;
            }

            if (_appendCountPerRound <= 0)
            {
                MessageBox.Show("Append count must be greater than 0");
                return;
            }

            _signalProducer.ClearPendingSignalRounds(_chartSignals);

            //Read X axis length
            try
            {
                _xLen = double.Parse(textBoxXLen.Text);
            }
            catch
            {
                MessageBox.Show("Invalid X-Axis length text input");
                return;
            }

            _chart.BeginUpdate();

            _chart.ViewXY.AxisLayout.AutoShrinkSegmentsGap = false;

            //Clear Data series
            DisposeAllAndClear(v.PointLineSeries);

            //Clear Y axes
            DisposeAllAndClear(v.YAxes);
            _chartSignals.Clear();

            //Series count of Y axes and data series
            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                _chartSignals.Add(CreateChartSignal(v, seriesIndex));
            }

            _signalProducer.Reset(_chartSignals, _appendCountPerRound, CurrentSampleIntervalSeconds);

            v.XAxes[0].SetRange(0, VisibleRangeSeconds);

            //Prefill with data, this may take several seconds  
            if (checkBoxPrefill.IsChecked == true)
            {
                PrefillChartWithData();
            }

            _chart.EndUpdate();
            _isStreaming = true;
            _producerTimer.Change(ProducerIntervalMs, ProducerIntervalMs);
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private ChartSignal CreateChartSignal(ViewXY view, int seriesIndex)
        {
            SignalValueKind kind = GetSignalKind(seriesIndex);
            ChartSignal signal = new ChartSignal(string.Format("Ch {0}", seriesIndex + 1), kind, YMin, YMax);
            Color lineBaseColor = DefaultColors.SeriesForBlackBackgroundWpf[seriesIndex % DefaultColors.SeriesForBlackBackgroundWpf.Length];

            AxisY axisY = new AxisY(view);
            if (kind == SignalValueKind.Analog)
            {
                axisY.SetRange(YMin, YMax);
            }
            else
            {
                axisY.SetRange(0, 1);
            }

            axisY.Title.Text = signal.Name;
            axisY.Title.Angle = 0;
            axisY.Title.Color = ChartTools.CalcGradient(lineBaseColor, System.Windows.Media.Colors.White, 50);
            axisY.Units.Visible = false;
            axisY.AllowScaling = false;
            axisY.MajorGrid.Visible = false;
            axisY.MinorGrid.Visible = false;
            axisY.MajorGrid.Pattern = LinePattern.Solid;
            axisY.AutoDivSeparationPercent = 0;
            axisY.Units.Text = "mV";
            axisY.Visible = true;
            axisY.MajorDivTickStyle.Alignment = Alignment.Near;
            axisY.Title.HorizontalAlign = YAxisTitleAlignmentHorizontal.Left;

            if (seriesIndex == _seriesCount - 1)
            {
                //Create a mini-scale for last axis, it's used when Y axes are hidden.
                axisY.MiniScale.ShowX = true;
                axisY.MiniScale.ShowY = true;
                axisY.MiniScale.Color = Color.FromArgb(255, 255, 204, 0);
                axisY.MiniScale.HorizontalAlign = AlignmentHorizontal.Right;
                axisY.MiniScale.VerticalAlign = AlignmentVertical.Bottom;
                axisY.MiniScale.Offset = new PointIntXY(-30, -30);
                axisY.MiniScale.LabelX.Color = Colors.White;
                axisY.MiniScale.LabelY.Color = Colors.White;
                axisY.MiniScale.PreferredSize = new Arction.Wpf.Charting.SizeDoubleXY(50, 50);
            }

            view.YAxes.Add(axisY);

            PointLineSeries series = new PointLineSeries(view, view.XAxes[0], axisY);
            view.PointLineSeries.Add(series);
            series.LineStyle.Color = ChartTools.CalcGradient(lineBaseColor, System.Windows.Media.Colors.White, 50);
            series.LineStyle.Width = LineWidth;
            series.AllowUserInteraction = false;
            series.PointsVisible = false;

            signal.AxisY = axisY;
            signal.Series = series;
            return signal;
        }

        private static SignalValueKind GetSignalKind(int seriesIndex)
        {
            if (seriesIndex == 0)
            {
                return SignalValueKind.StepDigital;
            }

            if (seriesIndex == 1)
            {
                return SignalValueKind.Digital;
            }

            return SignalValueKind.Analog;
        }

        /// <summary>
        /// Dispose items in collection before and clear.
        /// </summary>
        /// <typeparam name="T">Collection type</typeparam>
        /// <param name="list">Collection</param>

        public static void DisposeAllAndClear<T>(List<T> list) where T : IDisposable
        {
            if (list == null)
            {
                return;
            }

            while (list.Count > 0)
            {
                int lastInd = list.Count - 1;
                T item = list[lastInd]; // take item ref from list. 
                list.RemoveAt(lastInd); // remove item first
                if (item != null)
                {
                    (item as IDisposable).Dispose();     // then dispose it. 
                }
            }
        }

        private void ProducerTimerCallback(object state)
        {
            if (_isStreaming == false)
            {
                return;
            }

            lock (_signalProducer.SyncRoot)
            {
                if (_isStreaming == false)
                {
                    return;
                }

                _signalProducer.EnqueueSignalRound(_chartSignals);
            }
        }

        private void PrefillChartWithData()
        {
            int pointsToPrefill = (int)(0.9 * _xLen);
            int batchCount = pointsToPrefill / _appendCountPerRound;
            for (int i = 0; i < batchCount; i++)
            {
                ConsumeGeneratedSignalRound();
            }

            if (_pointsAppended > 0)
            {
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }
        }

        private void ConsumePendingSignalRounds(int maxRoundCount)
        {
            if (_chart == null)
            {
                return;
            }

            bool consumedAny = false;
            int consumedRoundCount = 0;

            _chart.BeginUpdate();
            try
            {
                double lastRoundX;
                while ((maxRoundCount <= 0 || consumedRoundCount < maxRoundCount) && TryDequeuePendingRound(out lastRoundX))
                {
                    ConsumePendingSignalRound(lastRoundX);
                    consumedAny = true;
                    consumedRoundCount++;
                }
            }
            finally
            {
                _chart.EndUpdate();
            }

            if (consumedAny)
            {
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }
        }

        private void ConsumeGeneratedSignalRound()
        {
            _lastConsumedX = _signalProducer.GenerateSignalRoundDirect(_chartSignals);
            _pointsAppended += _appendCountPerRound;
        }

        private void ConsumePendingSignalRound(double lastRoundX)
        {
            foreach (ChartSignal signal in _chartSignals)
            {
                SeriesPoint[] points;
                if (signal.TryDequeuePoints(out points) == false)
                {
                    continue;
                }

                if (points != null && points.Length > 0)
                {
                    signal.Series.AddPoints(points, false);
                }
            }

            _pointsAppended += _appendCountPerRound;
            _lastConsumedX = lastRoundX;
        }

        private bool TryDequeuePendingRound(out double lastRoundX)
        {
            return _signalProducer.TryDequeuePendingRound(out lastRoundX);
        }

        private void UpdateSweepBands(double lastX)
        {
            if (_chart.ViewXY.XAxes[0].ScrollMode == XAxisScrollMode.Sweeping)
            {

                //Dark band of old page fading away 
                double pageLen = _chart.ViewXY.XAxes[0].Maximum - _chart.ViewXY.XAxes[0].Minimum;
                double sweepGapWidth = pageLen / 20.0;
                _chart.ViewXY.Bands[0].SetValues(lastX - pageLen, lastX - pageLen + sweepGapWidth);
                if (_chart.ViewXY.Bands[0].Visible == false)
                {
                    _chart.ViewXY.Bands[0].Visible = true;
                }


                //Bright new page band
                _chart.ViewXY.Bands[1].SetValues(lastX - sweepGapWidth / 6, lastX);
                if (_chart.ViewXY.Bands[1].Visible == false)
                {
                    _chart.ViewXY.Bands[1].Visible = true;
                }
            }
            else
            {
                if (_chart.ViewXY.Bands[0].Visible == true)
                {
                    _chart.ViewXY.Bands[0].Visible = false;
                }

                if (_chart.ViewXY.Bands[1].Visible == true)
                {
                    _chart.ViewXY.Bands[1].Visible = false;
                }
            }
        }

        private void SetYAxisVisible(AxisY yAxis, bool visible)
        {
            yAxis.LabelsVisible = visible;
            yAxis.MajorDivTickStyle.Visible = visible;
            yAxis.MinorDivTickStyle.Visible = visible;
            yAxis.Title.Visible = visible;
            if (visible)
            {
                yAxis.AxisThickness = 3;
            }
            else
            {
                yAxis.AxisThickness = 0;
            }
        }

        private void comboBoxScrollMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_chart != null)
            {
                if (comboBoxScrollMode.SelectedIndex == 0)
                {
                    _chart.ViewXY.XAxes[0].ScrollMode = XAxisScrollMode.Scrolling;
                }
                else if (comboBoxScrollMode.SelectedIndex == 1)
                {
                    _chart.ViewXY.XAxes[0].ScrollMode = XAxisScrollMode.Sweeping;
                }
            }
        }

        private void buttonFollowScrollMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.FollowScroll);
        }

        private void buttonPageMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.Paging);
        }

        private void buttonFreeMode_Click(object sender, RoutedEventArgs e)
        {
            SetXAxisViewMode(XAxisViewMode.Free);
        }

        private void buttonZoomXPlus_Click(object sender, RoutedEventArgs e)
        {
            ZoomX(0.5);
        }


        private void buttonZoomXMinus_Click(object sender, RoutedEventArgs e)
        {
            ZoomX(2);
        }

        private void ZoomX(double factor, double? anchorX = null)
        {
            _chart.BeginUpdate();
            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double currentMin = xAxis.Minimum;
            double currentMax = xAxis.Maximum;
            double zoomAnchor = anchorX ?? ((currentMin + currentMax) / 2.0);

            if (zoomAnchor < currentMin)
            {
                zoomAnchor = currentMin;
            }
            else if (zoomAnchor > currentMax)
            {
                zoomAnchor = currentMax;
            }

            double newMin = zoomAnchor - (zoomAnchor - currentMin) * factor;
            double newMax = zoomAnchor + (currentMax - zoomAnchor) * factor;
            xAxis.SetRange(newMin, newMax);
            _chart.EndUpdate();
        }


        private void buttonZoomYPlus_Click(object sender, RoutedEventArgs e)
        {
            ZoomY(0.5);
        }

        private void buttonZoomYMinus_Click(object sender, RoutedEventArgs e)
        {
            ZoomY(2);
        }

        private void ZoomY(double factor)
        {
            _chart.BeginUpdate();
            foreach (AxisY yAxis in _chart.ViewXY.YAxes)
            {
                double yLen = yAxis.Maximum - yAxis.Minimum;
                double yLenNew = factor * yLen;
                double yCenter = (yAxis.Minimum + yAxis.Maximum) / 2.0;
                yAxis.SetRange(yCenter - yLenNew / 2.0, yCenter + yLenNew / 2.0);
            }
            _chart.EndUpdate();

        }

        private void buttonPointCount_Checked(object sender, RoutedEventArgs e)
        {
            ulong pointCount = 1000000;
            ulong seriesCount = 1;
            ulong xAxisPointCount = 1000;
            ulong appendPointsPerRound = 1000;

            if (sender == button1M)
            {
                pointCount = 1000000;
                seriesCount = 4;
            }
            else if (sender == button10M)
            {
                pointCount = 10000000;
                seriesCount = 4;
            }
            else if (sender == button100M)
            {
                pointCount = 100000000;
                seriesCount = 8;
            }
            else if (sender == button1000M)
            {
                pointCount = 1000000000;
                seriesCount = 16;
            }
            else if (sender == button2000M)
            {
                pointCount = 2000000000;
                seriesCount = 16;
            }
            else if (sender == button3000M)
            {
                pointCount = 3000000000;
                seriesCount = 16;
            }
            else if (sender == button4000M)
            {
                pointCount = 4000000000;
                seriesCount = 16;
            }
            else if (sender == button5000M)
            {
                pointCount = 5000000000;
                seriesCount = 16;
            }
            else if (sender == button6000M)
            {
                pointCount = 6000000000;
                seriesCount = 32;
            }
            else if (sender == button7000M)
            {
                pointCount = 7000000000;
                seriesCount = 32;
            }
            else if (sender == button8000M)
            {
                pointCount = 8000000000;
                seriesCount = 32;
            }

            xAxisPointCount = pointCount / seriesCount;
            appendPointsPerRound = 10;

            if (textBoxSeriesCount != null)
            {
                textBoxSeriesCount.Text = seriesCount.ToString();
            }

            if (textBoxAppendCountPerRound != null)
            {
                textBoxAppendCountPerRound.Text = appendPointsPerRound.ToString();
            }

            if (textBoxXLen != null)
            {
                textBoxXLen.Text = xAxisPointCount.ToString();
            }
        }

        private void checkBoxAxesVisible_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_chart != null && _chartSignals.Count > 0)
            {
                bool yAxesVisible = checkBoxAxesVisible.IsChecked == true;
                _chart.BeginUpdate();

                //Show miniscale only on last Y axis 
                AxisY lastYAxis = _chartSignals.Last().AxisY;

                _chart.ViewXY.AxisLayout.AutoAdjustMargins = false;

                foreach (ChartSignal signal in _chartSignals)
                {
                    AxisY yAxis = signal.AxisY;
                    if (yAxis != lastYAxis)
                    {
                        yAxis.Visible = yAxesVisible;
                    }
                    else
                    {
                        yAxis.Visible = true;
                        SetYAxisVisible(yAxis, yAxesVisible);
                        lastYAxis.MiniScale.Visible = !yAxesVisible;
                    }

                }
                _chart.EndUpdate();

            }

        }

        /// <summary>
        /// Call this method to stop threads, dispose unmanaged resources or 
        /// to perform any other job that needs to be done before this 
        /// example object is ready for garbage collector.
        /// </summary>
        public void Dispose()
        {
            Stop();
            if (_producerTimer != null)
            {
                _producerTimer.Dispose();
                _producerTimer = null;
            }
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            if (_chart != null)
            {
                _chart.Dispose();
                _chart = null;
            }
        }

        /// <summary>
        /// Start real-time monitoring.
        /// </summary>
        public void Stop()
        {
            _isStreaming = false;

            if (_producerTimer != null)
            {
                _producerTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _signalProducer.ClearPendingSignalRounds(_chartSignals);

        }

        /// <summary>
        /// Check timer status.
        /// </summary>
        internal bool IsRunning
        {
            get
            {
                return _isStreaming;
            }
        }

    }
}
