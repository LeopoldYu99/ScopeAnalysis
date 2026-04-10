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

        //StopWatch for controlling FPS calculation
        private System.Diagnostics.Stopwatch _stopWatch;

        //Series count 
        private int _seriesCount;

        //Append data point per round count
        private int _appendCountPerRound;

        //X axis length 
        private double _xLen;

        //X data point step
        private const double XInterval = 1;

        //Y axis minimum 
        private const double YMin = 0;

        //Y axis maximum 
        private const double YMax = 100;

        //Generate this many rounds of data 
        private const int PreGenerateDataForRoundCount = 200;

        //Data array for each series 
        private float[][] _data;

        //Data feeding round 
        private int _iRound = 0;

        //Points appended thus far 
        private long _pointsAppended;

        //Frames rendered thus far 
        private int _framesRenderedCount = 0;

        //Line width in pixels 
        private const float LineWidth = 1f;

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
            _stopWatch = new System.Diagnostics.Stopwatch();
            UpdateXAxisViewModeButtons();
            CreateChart();
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
            view.XAxes[0].LabelsNumberFormat = "N0";
            view.XAxes[0].Title.Text = "Point number";
            view.XAxes[0].SetRange(0, 100000);
            view.XAxes[0].MajorGrid.Pattern = LinePattern.Solid;
            view.XAxes[0].Units.Text = "Points";

            //Set real-time monitoring automatic old data destruction
            view.DropOldSeriesData = true;

            //Set Axis layout to Segmented
            view.AxisLayout.YAxesLayout = YAxesLayout.Stacked;
            view.AxisLayout.SegmentsGap = 2;
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
                ZoomX(zoomFactor);
                e.Handled = true;
            }
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
            UpdateXAxisView(_pointsAppended * XInterval);
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

            //Start
            _iRound = 0;
            _pointsAppended = 0;
            _framesRenderedCount = 0;

            _stopWatch.Stop(); //this is started in the CompositionTarget_Rendering in first round 

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
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

            //Create all data to be inputted before starting the monitoring, 
            //to prevent data points generating to interrupt

            _data = CreateInputData(_seriesCount, _appendCountPerRound);

            _chart.BeginUpdate();

            _chart.ViewXY.AxisLayout.AutoShrinkSegmentsGap = true;

            //Clear Data series
            DisposeAllAndClear(v.SampleDataBlockSeries);

            //Clear Y axes
            DisposeAllAndClear(v.YAxes);

            //Series count of Y axes and SampleDataBlockSeries  
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

                SampleDataBlockSeries series = new SampleDataBlockSeries(v, v.XAxes[0], axisY);
                v.SampleDataBlockSeries.Add(series);
                series.Color = ChartTools.CalcGradient(lineBaseColor, System.Windows.Media.Colors.White, 50);

                series.SamplingFrequency = 1.0 / XInterval; //Set 1 / X interval here 
                series.FirstSampleTimeStamp = 1.0 / series.SamplingFrequency;//Set first X here 
                series.ScrollModePointsKeepLevel = 1;
                series.AllowUserInteraction = false;
            }

            v.XAxes[0].SetRange(0, _xLen);

            //Prefill with data, this may take several seconds  
            if (checkBoxPrefill.IsChecked == true)
            {
                PrefillChartWithData();
            }


            _chart.EndUpdate();
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

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            //Frame rendered. Update stats and start next update round. 
            _framesRenderedCount++;

            TotalDataPoints.Content = "Total data points: " + (_pointsAppended * _seriesCount).ToString("N0");

            RenderNextFrame();
        }

        private void RenderNextFrame()
        {
            if (_chart == null)
            {
                return;
            }

            if (_stopWatch.IsRunning == false)
            {
                _stopWatch.Restart();
            }
            Dispatcher.Invoke(() =>
            {
                FpsCounter.Content = "Fps: " + (_framesRenderedCount / (double)_stopWatch.ElapsedMilliseconds * 1000.0).ToString("0.0");
                DataPointsInVisibleArea.Content = "Visible data points: " + (Math.Min(_chart.ViewXY.XAxes[0].Maximum - _chart.ViewXY.XAxes[0].Minimum, _pointsAppended) * _seriesCount).ToString("N0");

                FeedData(/*chartTitleText*/);
            });
        }

        private float[][] CreateInputData(int seriesCount, int appendCountPerRound)
        {
            //Create input data for all series. 
            float[][] data = new float[seriesCount][];
          //  System.Threading.Tasks.Parallel.For(0, seriesCount, (seriesIndex) =>
            for(int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
            {
                
                int dataPointCount = PreGenerateDataForRoundCount * appendCountPerRound;
                float[] seriesData = new float[dataPointCount];
                float seriesIndexPlus1 = seriesIndex + 1;
                Random rand = new Random((int)DateTime.Now.Ticks / (seriesIndex + 1));
                if (seriesIndex == 0)
                {
                    for (int i = 0; i < dataPointCount; i++)
                    {
                        seriesData[i] = rand.Next(0, 2);
                    }

                    data[seriesIndex] = seriesData;
                    continue;
                }
                else if (seriesIndex == 1)
                {
                    for (int i = 0; i < dataPointCount; i++)
                    {
                        seriesData[i] = rand.Next(0, 2);
                    }

                    data[seriesIndex] = seriesData;
                    continue;
                }

                double y = 50;
                for (int i = 0; i < dataPointCount; i++)
                {
                    y = y - 0.05 + rand.NextDouble() / 10.0;
                    if (y > YMax)
                    {
                        y = YMax;
                    }

                    if (y < YMin)
                    {
                        y = YMin;
                    }

                    seriesData[i] = (float)y;
                }
                data[seriesIndex] = seriesData;
            }//);

            return data;
        }

        private void PrefillChartWithData()
        {
            //Set data almost till the end, 
            //so it will reach end and start scrolling quite soon. 

            //How many rounds to prefill in the series 
            int roundsToPrefill = (int)(0.9 * _xLen) / _appendCountPerRound;

            //How many points to prefill in the series
            int pointCount = roundsToPrefill * _appendCountPerRound;

            System.Threading.Tasks.Parallel.For(0, _seriesCount, (seriesIndex) =>
            {
                float[] thisSeriesData = _data[seriesIndex];

                for (int round = 0; round < roundsToPrefill; round++)
                {
                    float[] dataArray = new float[_appendCountPerRound];
                    Array.Copy(thisSeriesData, (round % PreGenerateDataForRoundCount) * _appendCountPerRound, dataArray, 0, _appendCountPerRound);
                    _chart.ViewXY.SampleDataBlockSeries[seriesIndex].AddSamples(dataArray, false);
                }
            });

            _pointsAppended += pointCount;
            _iRound += roundsToPrefill;

            //Set X axis real-time scrolling position 
            double lastX = _pointsAppended * XInterval;
            UpdateXAxisView(lastX);
        }

        private long feeddata = 0;

        private void FeedData(/*string chartTitleText*/)
        {
            feeddata = _stopWatch.ElapsedMilliseconds;
            if (_chart != null)
            {
                _chart.BeginUpdate();

                //Append data to series
                System.Threading.Tasks.Parallel.For(0, _seriesCount, (seriesIndex) =>
                {
                    float[] thisSeriesData = _data[seriesIndex];
                    float[] dataToAppendNow = new float[_appendCountPerRound];
                    Array.Copy(thisSeriesData, (_iRound % PreGenerateDataForRoundCount) * _appendCountPerRound, dataToAppendNow, 0, _appendCountPerRound);
                    _chart.ViewXY.SampleDataBlockSeries[seriesIndex].AddSamples(dataToAppendNow, false);
                });

                _pointsAppended += _appendCountPerRound;

                //Set X axis real-time scrolling position 
                double lastX = _pointsAppended * XInterval;
                UpdateXAxisView(lastX);

                //Update sweep bands
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
                    //Hide sweeping bands if not in sweeping mode
                    if (_chart.ViewXY.Bands[0].Visible == true)
                    {
                        _chart.ViewXY.Bands[0].Visible = false;
                    }

                    if (_chart.ViewXY.Bands[1].Visible == true)
                    {
                        _chart.ViewXY.Bands[1].Visible = false;
                    }
                }
                _chart.EndUpdate();

                _iRound++;
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

        private void ZoomX(double factor)
        {
            _chart.BeginUpdate();
            AxisX xAxis = _chart.ViewXY.XAxes[0];
            double xLen = xAxis.Maximum - xAxis.Minimum;
            xAxis.SetRange(xAxis.ScrollPosition - xLen * factor, xAxis.ScrollPosition);
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
            appendPointsPerRound = xAxisPointCount / 5000;

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
            if (IsRunning == true)
            {
                _stopWatch.Stop();
            }
        }

        /// <summary>
        /// Check timer status.
        /// </summary>
        internal bool IsRunning
        {
            get
            {
                return _stopWatch.IsRunning;
            }
        }

    }
}
