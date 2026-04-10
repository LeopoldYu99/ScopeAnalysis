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
using System.Collections.Concurrent;
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
        private sealed class ChartUpdateBatch
        {
            public ChartUpdateBatch(int seriesCount, int sampleCount)
            {
                SampleCount = sampleCount;
                SeriesPoints = new SeriesPoint[seriesCount][];
            }

            public int SampleCount { get; private set; }
            public SeriesPoint[][] SeriesPoints { get; private set; }
            public double FirstTime { get; set; }
            public double LastTime { get; set; }
        }

        //The chart
        private LightningChart _chart;


        //Background producer timer
        private Timer _producerTimer;

        //Producer / consumer queue
        private readonly ConcurrentQueue<ChartUpdateBatch> _pendingBatches = new ConcurrentQueue<ChartUpdateBatch>();
        private readonly object _producerSync = new object();
        private Random[] _seriesRandoms;
        private double[] _seriesValueState;
        private long _producedPointCount;
        private double _nextSampleTimeSeconds;
        private double _lastConsumedX;
        private bool _isStreaming;
        private long _pendingBatchCount;
        private long _pendingSampleCount;

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

        //Digital series state for real timestamp step rendering.
        private bool _hasDigitalValue;
        private float _previousDigitalValue;

        //Points appended thus far 
        private long _pointsAppended;

        //Line width in pixels 
        private const float LineWidth = 1f;
        private const int ProducerIntervalMs = 50;
        private const int DefaultAppendCountPerRound = 10;
        private const int MaxBatchesPerRender = 4;

        private const int DigitalSeriesIndex = 0;

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

            ConsumePendingBatches(MaxBatchesPerRender);
            FpsCounter.Content = string.Format("Queue: {0} batches / {1:N0} points", Interlocked.Read(ref _pendingBatchCount), Interlocked.Read(ref _pendingSampleCount));
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
            _producedPointCount = 0;
            _nextSampleTimeSeconds = 0;
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

            _hasDigitalValue = false;
            _previousDigitalValue = 0;
            ClearPendingBatches();

            InitializeProducerState();

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

            //Series count of Y axes and data series  
            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                Color lineBaseColor = DefaultColors.SeriesForBlackBackgroundWpf[seriesIndex % DefaultColors.SeriesForBlackBackgroundWpf.Length];

                AxisY axisY = new AxisY(v);
                if(seriesIndex == 0)
                {
                    axisY.SetRange(0, 1);
                }
                else if(seriesIndex == 1)
                {
                    axisY.SetRange(0, 1);
                }
                else
                {
                    axisY.SetRange(YMin, YMax);
                }


                axisY.Title.Text = string.Format("Ch {0}", seriesIndex + 1);
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
                v.YAxes.Add(axisY);

                PointLineSeries series = new PointLineSeries(v, v.XAxes[0], axisY);
                v.PointLineSeries.Add(series);
                series.LineStyle.Color = ChartTools.CalcGradient(lineBaseColor, System.Windows.Media.Colors.White, 50);
                series.LineStyle.Width = LineWidth;
                series.AllowUserInteraction = false;
                series.PointsVisible = false;
            }

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

        private void InitializeProducerState()
        {
            _seriesRandoms = new Random[_seriesCount];
            _seriesValueState = new double[_seriesCount];

            int seedBase = Environment.TickCount;
            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                _seriesRandoms[seriesIndex] = new Random(seedBase + seriesIndex * 97);
                _seriesValueState[seriesIndex] = 50;
            }
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

            lock (_producerSync)
            {
                if (_isStreaming == false)
                {
                    return;
                }

                EnqueuePendingBatch(CreateChartUpdateBatch());
            }
        }

        private ChartUpdateBatch CreateChartUpdateBatch()
        {
            ChartUpdateBatch batch = new ChartUpdateBatch(_seriesCount, _appendCountPerRound);
            List<SeriesPoint> digitalPoints = new List<SeriesPoint>(_appendCountPerRound * 2);
            double sampleIntervalSeconds = CurrentSampleIntervalSeconds;
            batch.FirstTime = _nextSampleTimeSeconds;

            for (int seriesIndex = 1; seriesIndex < _seriesCount; seriesIndex++)
            {
                batch.SeriesPoints[seriesIndex] = new SeriesPoint[_appendCountPerRound];
            }

            for (int pointIndex = 0; pointIndex < _appendCountPerRound; pointIndex++)
            {
                double time = _nextSampleTimeSeconds;
                float digitalValue = GenerateTestValue(DigitalSeriesIndex);

                if (_hasDigitalValue == false)
                {
                    digitalPoints.Add(new SeriesPoint(time, digitalValue));
                    _hasDigitalValue = true;
                }
                else
                {
                    digitalPoints.Add(new SeriesPoint(time, _previousDigitalValue));
                    if (Math.Abs(digitalValue - _previousDigitalValue) > float.Epsilon)
                    {
                        digitalPoints.Add(new SeriesPoint(time, digitalValue));
                    }
                }

                _previousDigitalValue = digitalValue;
                batch.LastTime = time;

                for (int seriesIndex = 1; seriesIndex < _seriesCount; seriesIndex++)
                {
                    batch.SeriesPoints[seriesIndex][pointIndex] = new SeriesPoint(time, GenerateTestValue(seriesIndex));
                }

                _producedPointCount++;
                _nextSampleTimeSeconds += sampleIntervalSeconds;
            }

            batch.SeriesPoints[DigitalSeriesIndex] = digitalPoints.ToArray();
            return batch;
        }

        private float GenerateTestValue(int seriesIndex)
        {
            if (seriesIndex == 0 || seriesIndex == 1)
            {
                return _seriesRandoms[seriesIndex].Next(0, 2);
            }

            double y = _seriesValueState[seriesIndex];
            y = y - 0.05 + _seriesRandoms[seriesIndex].NextDouble() / 10.0;
            y = Math.Max(YMin, Math.Min(YMax, y));
            _seriesValueState[seriesIndex] = y;
            return (float)y;
        }

        private void PrefillChartWithData()
        {
            int pointsToPrefill = (int)(0.9 * _xLen);
            int batchCount = pointsToPrefill / _appendCountPerRound;
            for (int i = 0; i < batchCount; i++)
            {
                ConsumeBatch(CreateChartUpdateBatch());
            }

            if (_pointsAppended > 0)
            {
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }
        }

        private void ConsumePendingBatches(int maxBatchCount)
        {
            if (_chart == null)
            {
                return;
            }

            ChartUpdateBatch batch;
            bool consumedAny = false;
            int consumedBatchCount = 0;

            _chart.BeginUpdate();
            try
            {
                while ((maxBatchCount <= 0 || consumedBatchCount < maxBatchCount) && TryDequeuePendingBatch(out batch))
                {
                    ConsumeBatch(batch);
                    consumedAny = true;
                    consumedBatchCount++;
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

        private void ConsumeBatch(ChartUpdateBatch batch)
        {
            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                SeriesPoint[] points = batch.SeriesPoints[seriesIndex];
                if (points != null && points.Length > 0)
                {
                    _chart.ViewXY.PointLineSeries[seriesIndex].AddPoints(points, false);
                }
            }

            _pointsAppended += batch.SampleCount;
            _lastConsumedX = batch.LastTime;
        }

        private void EnqueuePendingBatch(ChartUpdateBatch batch)
        {
            if (batch == null)
            {
                return;
            }

            _pendingBatches.Enqueue(batch);
            Interlocked.Increment(ref _pendingBatchCount);
            Interlocked.Add(ref _pendingSampleCount, batch.SampleCount);
        }

        private bool TryDequeuePendingBatch(out ChartUpdateBatch batch)
        {
            if (_pendingBatches.TryDequeue(out batch) == false)
            {
                return false;
            }

            Interlocked.Decrement(ref _pendingBatchCount);
            Interlocked.Add(ref _pendingSampleCount, -batch.SampleCount);
            return true;
        }

        private void ClearPendingBatches()
        {
            lock (_producerSync)
            {
                ChartUpdateBatch batch;
                while (_pendingBatches.TryDequeue(out batch))
                {
                }
            }

            Interlocked.Exchange(ref _pendingBatchCount, 0);
            Interlocked.Exchange(ref _pendingSampleCount, 0);
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
            if (_chart != null)
            {
                bool yAxesVisible = checkBoxAxesVisible.IsChecked == true;
                _chart.BeginUpdate();

                //Show miniscale only on last Y axis 
                AxisY lastYAxis = _chart.ViewXY.YAxes.Last();

                _chart.ViewXY.AxisLayout.AutoAdjustMargins = false;

                foreach (AxisY yAxis in _chart.ViewXY.YAxes)
                {
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
            ClearPendingBatches();

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
