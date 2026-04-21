using Arction.Wpf.Charting.Views.ViewXY;
using Arction.Wpf.Charting.SeriesXY;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Arction.Wpf.Charting;
using LCWpf;
using Microsoft.Win32;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
    {
        private sealed class SignalImportSelection
        {
            public string ProtocolName { get; set; }
            public string FilePath { get; set; }
        }

        public void Start()
        {
            Stop();

            _hasConsumedData = false;
            _lastConsumedX = 0;
            _isCursorHovering = false;
            _hoverMeasurementXValue = 0;
            _cursorMeasurementSignal = null;

            ViewXY view = _chart.ViewXY;



            _signalProducer.ClearPendingData(_chartSignals);

 

            _chart.BeginUpdate();
            _chart.ViewXY.AxisLayout.AutoShrinkSegmentsGap = false;
            _decodeCache.Clear();
            InvalidateOverlayCaches();

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
                view.XAxes[0].SetRange(0, _xLen * CurrentSampleIntervalSeconds);
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
            signal.AppendRecentPoints(points);

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

        private void ShowDataProducerDialog()
        {
            SerialPortDataProducer producer = new SerialPortDataProducer();

            Window dialog = new Window
            {
                Title = "DataProducer",
                Owner = this,
                Width = 860,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White,
                Content = BuildDataProducerDialogContent(producer)
            };

            dialog.ShowDialog();
        }

        private static UIElement BuildDataProducerDialogContent(SerialPortDataProducer producer)
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

            Button closeButton = new Button
            {
                Content = "Close",
                Width = 96,
                Height = 32,
                IsCancel = true
            };
            closeButton.Click += (sender, e) =>
            {
                Window dialog = Window.GetWindow((DependencyObject)sender);
                if (dialog != null)
                {
                    dialog.DialogResult = false;
                }
            };

            buttonBar.Children.Add(closeButton);
            Grid.SetRow(buttonBar, 1);
            layoutRoot.Children.Add(buttonBar);

            return layoutRoot;
        }

        private void ShowSignalImportDialogForSignal(int signalIndex)
        {
            if (_chart == null || signalIndex < 0 || signalIndex >= _chartSignals.Count)
            {
                return;
            }

            string signalName = _chartSignals[signalIndex].Name;
            SignalImportSelection selection = null;

            Window dialog = new Window
            {
                Title = string.Format("{0} Import", signalName),
                Owner = this,
                Width = 560,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White
            };

            Grid layoutRoot = new Grid
            {
                Margin = new Thickness(16)
            };
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layoutRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock protocolLabel = new TextBlock
            {
                Text = "Protocol:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetRow(protocolLabel, 0);
            Grid.SetColumn(protocolLabel, 0);
            layoutRoot.Children.Add(protocolLabel);

            ComboBox protocolComboBox = new ComboBox
            {
                MinWidth = 220,
                SelectedIndex = 0
            };
            protocolComboBox.Items.Add("串口");
            protocolComboBox.Items.Add("2线串口");
            protocolComboBox.Items.Add("3线串口");
            protocolComboBox.Items.Add("4线串口");
            Grid.SetRow(protocolComboBox, 0);
            Grid.SetColumn(protocolComboBox, 1);
            Grid.SetColumnSpan(protocolComboBox, 2);
            layoutRoot.Children.Add(protocolComboBox);

            TextBlock fileLabel = new TextBlock
            {
                Text = "File:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 10, 0)
            };
            Grid.SetRow(fileLabel, 1);
            Grid.SetColumn(fileLabel, 0);
            layoutRoot.Children.Add(fileLabel);

            TextBox filePathTextBox = new TextBox
            {
                Margin = new Thickness(0, 12, 10, 0),
                MinWidth = 220
            };
            Grid.SetRow(filePathTextBox, 1);
            Grid.SetColumn(filePathTextBox, 1);
            layoutRoot.Children.Add(filePathTextBox);

            Button browseButton = new Button
            {
                Content = "Browse...",
                Width = 96,
                Height = 28,
                Margin = new Thickness(0, 12, 0, 0)
            };
            browseButton.Click += (sender, e) =>
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog(dialog) == true)
                {
                    filePathTextBox.Text = openFileDialog.FileName;
                }
            };
            Grid.SetRow(browseButton, 1);
            Grid.SetColumn(browseButton, 2);
            layoutRoot.Children.Add(browseButton);

            TextBlock hintTextBlock = new TextBlock
            {
                Text = "当前只完成协议和文件选择流程，实际导入逻辑后续再接入。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(hintTextBlock, 2);
            Grid.SetColumnSpan(hintTextBlock, 3);
            layoutRoot.Children.Add(hintTextBlock);

            StackPanel buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
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
                string selectedProtocol = protocolComboBox.SelectedItem as string;
                string filePath = filePathTextBox.Text == null ? string.Empty : filePathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(selectedProtocol))
                {
                    MessageBox.Show(dialog, "Please select a protocol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show(dialog, "Please select a file.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (File.Exists(filePath) == false)
                {
                    MessageBox.Show(dialog, "Selected file does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                selection = new SignalImportSelection
                {
                    ProtocolName = selectedProtocol,
                    FilePath = filePath
                };
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
                dialog.DialogResult = false;
            };

            buttonBar.Children.Add(importButton);
            buttonBar.Children.Add(cancelButton);
            Grid.SetRow(buttonBar, 3);
            Grid.SetColumnSpan(buttonBar, 3);
            layoutRoot.Children.Add(buttonBar);

            dialog.Content = layoutRoot;

            bool? result = dialog.ShowDialog();
            if (result == true && selection != null)
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        "已为 {0} 选择导入配置。{1}{1}协议: {2}{1}文件: {3}{1}{1}当前仅完成弹窗和选择流程，具体导入逻辑暂未实现。",
                        signalName,
                        Environment.NewLine,
                        selection.ProtocolName,
                        selection.FilePath),
                    "Import Placeholder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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
                signal.AppendRecentPoints(points);

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

        private bool ConsumePendingSignalData()
        {
            if (_chart == null)
            {
                return false;
            }

            _chart.BeginUpdate();
            try
            {
                return ConsumePendingSignalBatch();
            }
            finally
            {
                _chart.EndUpdate();
            }
        }

        private bool ConsumePendingSignalBatch()
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
                    signal.AppendRecentPoints(points);

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
                MarkDecodeOverlayDirty();
                MarkCursorVisualDirty();
                UpdateXAxisView(_lastConsumedX);
                UpdateSweepBands(_lastConsumedX);
            }

            return hasConsumedPoint;
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
            _isCursorHovering = false;
            _hoverMeasurementXValue = 0;
            _cursorMeasurementSignal = null;
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
