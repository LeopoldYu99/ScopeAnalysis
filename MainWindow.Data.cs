using Arction.Wpf.Charting.Views.ViewXY;
using Arction.Wpf.Charting.SeriesXY;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Arction.Wpf.Charting;
using LCWpf;

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
            _decodeCache.Clear();

            DisposeAllAndClear(view.PointLineSeries);
            DisposeAllAndClear(view.YAxes);
            _chartSignals.Clear();

            int signalCount = _seriesCount;
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
            return true;
        }

        private void ShowSignalGeneratorForSignal(int signalIndex)
        {
            if (_chart == null || signalIndex < 0 || signalIndex >= _chartSignals.Count)
            {
                return;
            }

            SerialPortDataProducer producer = new SerialPortDataProducer();
            SerialWaveformBuildResult buildResult = null;

            Window dialog = new Window
            {
                Title = string.Format("{0} Generator", _chartSignals[signalIndex].Name),
                Owner = this,
                Width = 820,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White,
                Content = BuildSignalGeneratorDialogContent(producer, dialogResult =>
                {
                    if (dialogResult == false)
                    {
                        return false;
                    }

                    if (producer.TryBuildWaveform(out buildResult, out string errorMessage) == false)
                    {
                        MessageBox.Show(this, errorMessage ?? "Unable to generate waveform.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    return true;
                })
            };

            bool? result = dialog.ShowDialog();
            if (result != true || buildResult == null)
            {
                return;
            }

            ImportGeneratedWaveformToSignal(signalIndex, buildResult);
        }

        private UIElement BuildSignalGeneratorDialogContent(SerialPortDataProducer producer, Func<bool, bool> validateClose)
        {
            Grid layoutRoot = new Grid
            {
                Margin = new Thickness(12)
            };
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(producer, 0);
            layoutRoot.Children.Add(producer);

            StackPanel buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Button importButton = new Button
            {
                Content = "Import",
                Width = 96,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            importButton.Click += (sender, e) =>
            {
                Window dialog = Window.GetWindow((DependencyObject)sender);
                if (dialog == null || validateClose(true) == false)
                {
                    return;
                }

                dialog.DialogResult = true;
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 32,
                IsCancel = true
            };
            cancelButton.Click += (sender, e) =>
            {
                Window dialog = Window.GetWindow((DependencyObject)sender);
                if (dialog == null || validateClose(false) == false)
                {
                    return;
                }

                dialog.DialogResult = false;
            };

            buttonBar.Children.Add(importButton);
            buttonBar.Children.Add(cancelButton);
            Grid.SetRow(buttonBar, 1);
            layoutRoot.Children.Add(buttonBar);

            return layoutRoot;
        }

        private void ImportGeneratedWaveformToSignal(int signalIndex, SerialWaveformBuildResult buildResult)
        {
            if (_chart == null
                || buildResult == null
                || buildResult.WaveData == null
                || buildResult.WaveData.Length == 0
                || signalIndex < 0
                || signalIndex >= _chartSignals.Count
                || buildResult.SampleRate == 0)
            {
                return;
            }

            ChartSignal signal = _chartSignals[signalIndex];
            double sampleInterval = MicrosecondsPerSecond / buildResult.SampleRate;
            BinaryWaveformImportResult importResult = BinaryWaveformImporter.ImportBytes(signal.Name, buildResult.WaveData, sampleInterval);
            if (importResult == null || importResult.Points == null || importResult.Points.Length == 0)
            {
                MessageBox.Show(this, "Generated waveform is empty.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Stop();
            _isStreaming = false;

            _chart.BeginUpdate();
            try
            {
                ConfigureChartDataSourceBehavior(_chart.ViewXY, true);
                _decodeCache.Remove(signal);

                signal.ClearPendingChunks();
                signal.ClearRecentPoints();
                signal.Series.Clear();

                ApplyDecodeSettings(signal, buildResult);

                SeriesPoint[] points = importResult.Points;
                signal.AxisY.Title.Text = signal.Name;
                signal.Series.AddPoints(points, false);

                double keepSeconds = Math.Max(VisibleRangeSeconds * 6.0, points[points.Length - 1].X + sampleInterval);
                signal.AppendRecentPoints(points, keepSeconds);

                _lastConsumedX = points[points.Length - 1].X;
                _hasConsumedData = true;

                double rangeMax = Math.Max(sampleInterval, _lastConsumedX);
                _chart.ViewXY.XAxes[0].SetRange(0, rangeMax);
            }
            finally
            {
                _chart.EndUpdate();
            }

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            UpdateDecodeOverlay();
            UpdateCursorVisual();
        }

        private static void ApplyDecodeSettings(ChartSignal signal, SerialWaveformBuildResult buildResult)
        {
            if (signal == null || signal.DecodeSettings == null || buildResult == null)
            {
                return;
            }

            signal.DecodeSettings.BaudRate = buildResult.BaudRate;
            signal.DecodeSettings.DataBits = buildResult.DataBits;
            signal.DecodeSettings.StopBits = buildResult.StopBits;
            signal.DecodeSettings.ParityMode = ConvertParityMode(buildResult.Parity);
            signal.DecodeSettings.IdleBits = buildResult.IdleBits;
        }

        private static UartParityMode ConvertParityMode(ParityMode parityMode)
        {
            switch (parityMode)
            {
                case ParityMode.Odd:
                    return UartParityMode.Odd;
                case ParityMode.Even:
                    return UartParityMode.Even;
                case ParityMode.Mark:
                    return UartParityMode.Mark;
                case ParityMode.Space:
                    return UartParityMode.Space;
                default:
                    return UartParityMode.None;
            }
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
