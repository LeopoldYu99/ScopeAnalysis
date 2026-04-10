using Arction.Wpf.Charting.Views.ViewXY;
using Arction.Wpf.Charting.SeriesXY;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Arction.Wpf.Charting;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
    {
        public void Start()
        {
            Stop();

            _hasConsumedData = false;
            _lastConsumedX = 0;

            ViewXY view = _chart.ViewXY;

            try
            {
                _seriesCount = int.Parse(textBoxSeriesCount.Text);
            }
            catch
            {
                MessageBox.Show("Invalid series count text input");
                return;
            }

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

            _signalProducer.ClearPendingData(_chartSignals);

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

            DisposeAllAndClear(view.PointLineSeries);
            DisposeAllAndClear(view.YAxes);
            _chartSignals.Clear();

            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                _chartSignals.Add(CreateChartSignal(view, seriesIndex));
            }

            _signalProducer.Reset(_chartSignals, _appendCountPerRound, CurrentSampleIntervalSeconds);

            view.XAxes[0].SetRange(0, VisibleRangeSeconds);



            _chart.EndUpdate();
            _isStreaming = true;
            _producerTimer.Change(ProducerIntervalMs, ProducerIntervalMs);
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        public static void DisposeAllAndClear<T>(List<T> list) where T : IDisposable
        {
            if (list == null)
            {
                return;
            }

            while (list.Count > 0)
            {
                int lastIndex = list.Count - 1;
                T item = list[lastIndex];
                list.RemoveAt(lastIndex);
                if (item != null)
                {
                    (item as IDisposable).Dispose();
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

                _signalProducer.EnqueueSignalData(_chartSignals);
            }
        }

        private void PrefillChartWithData()
        {
            int pointsToPrefill = (int)(0.9 * _xLen);
            int batchCount = pointsToPrefill / _appendCountPerRound;
            for (int i = 0; i < batchCount; i++)
            {
                _signalProducer.EnqueueSignalData(_chartSignals);
                ConsumePendingSignalBatch();
            }

            if (_hasConsumedData)
            {
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }
        }

        private void ConsumePendingSignalData()
        {
            if (_chart == null)
            {
                return;
            }

            _chart.BeginUpdate();
            try
            {
                ConsumePendingSignalBatch();
            }
            finally
            {
                _chart.EndUpdate();
            }

            if (_hasConsumedData)
            {
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }
        }

        private void ConsumePendingSignalBatch()
        {
            double maxX = double.MinValue;
            bool hasConsumedPoint = false;

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

                    for (int i = 0; i < points.Length; i++)
                    {
                        if (hasConsumedPoint == false || points[i].X > maxX)
                        {
                            maxX = points[i].X;
                            hasConsumedPoint = true;
                        }
                    }
                }
            }

            if (hasConsumedPoint)
            {
                _lastConsumedX = maxX;
                _hasConsumedData = true;
            }
        }

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

        public void Stop()
        {
            _isStreaming = false;

            if (_producerTimer != null)
            {
                _producerTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _signalProducer.ClearPendingData(_chartSignals);
        }

        internal bool IsRunning
        {
            get
            {
                return _isStreaming;
            }
        }
    }
}
