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



            _signalProducer.ClearPendingData(_chartSignals);

 

            _chart.BeginUpdate();
            _chart.ViewXY.AxisLayout.AutoShrinkSegmentsGap = false;

            DisposeAllAndClear(view.PointLineSeries);
            DisposeAllAndClear(view.YAxes);
            _chartSignals.Clear();

            int signalCount = UseBinaryFileDataSource ? 1 : _seriesCount;
            for (int seriesIndex = 0; seriesIndex < signalCount; seriesIndex++)
            {
                _chartSignals.Add(CreateChartSignal(view, seriesIndex));
            }

            bool loadedFromBinaryFile = false;
            if (UseBinaryFileDataSource)
            {
                ConfigureChartDataSourceBehavior(view, true);
                loadedFromBinaryFile = LoadSignalsFromBinaryFile(view);
                if (loadedFromBinaryFile == false)
                {
                    ConfigureChartDataSourceBehavior(view, false);
                }
            }
            else
            {
                ConfigureChartDataSourceBehavior(view, false);
            }

            if (loadedFromBinaryFile == false)
            {
                _signalProducer.Reset(_chartSignals, _appendCountPerRound, CurrentSampleIntervalSeconds);
                view.XAxes[0].SetRange(0, VisibleRangeSeconds);
            }

            _chart.EndUpdate();
            _isStreaming = loadedFromBinaryFile == false;

            if (_isStreaming)
            {
                _producerTimer.Change(ProducerIntervalMs, ProducerIntervalMs);
            }

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private static void ConfigureChartDataSourceBehavior(ViewXY view, bool useStaticImportedData)
        {
            if (view == null)
            {
                return;
            }

            view.DropOldSeriesData = useStaticImportedData == false;
            view.XAxes[0].ScrollMode = useStaticImportedData ? XAxisScrollMode.None : XAxisScrollMode.Scrolling;
        }

        private bool LoadSignalsFromBinaryFile(ViewXY view)
        {
            if (_chartSignals.Count == 0)
            {
                return false;
            }

            BinaryWaveformImportResult importResult = BinaryWaveformImporter.ImportFile( BinaryWaveFilePath,  MicrosecondsPerSecond / ImportedSampleRate);
            if (importResult == null || importResult.Points == null || importResult.Points.Length == 0)
            {
                return false;
            }

            ChartSignal signal = _chartSignals[0];
            SeriesPoint[] points = importResult.Points;
            signal.AxisY.Title.Text = importResult.SignalName;

            signal.Series.AddPoints(points, false);

            double keepSeconds = Math.Max(VisibleRangeSeconds * 6.0, points[points.Length - 1].X + CurrentSampleIntervalSeconds);
            signal.AppendRecentPoints(points, keepSeconds);

            _lastConsumedX = points[points.Length - 1].X;
            _hasConsumedData = true;

            double rangeMax = Math.Max(CurrentSampleIntervalSeconds, _lastConsumedX);
            view.XAxes[0].SetRange(0, rangeMax);
            SetXAxisViewMode(XAxisViewMode.Free);
            return true;
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
                    signal.AppendRecentPoints(points, Math.Max(VisibleRangeSeconds * 6.0, 2.0));

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
