using Arction.Wpf.Charting;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using LCWpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
    {
        private sealed class SignalImportSelection
        {
            public SerialProtocolType ProtocolType { get; set; }
            public string ProtocolName { get; set; }
            public string FilePath { get; set; }
            public uint SampleRate { get; set; }
        }

        public void Start()
        {
            Stop();

            _lastConsumedX = 0;
            _isMeasurementHovering = false;
            _measurementHoverXValue = 0;
            _measurementSignal = null;

            ViewXY view = _chart.ViewXY;

            _chart.BeginUpdate();
            _chart.ViewXY.AxisLayout.AutoShrinkSegmentsGap = false;
            _decodeCache.Clear();
            _measurementCache.Clear();
            InvalidateOverlayCaches();

            DisposeAllAndClear(view.DigitalLineSeries);
            DisposeAllAndClear(view.YAxes);
            _chartSignals.Clear();

            for (int seriesIndex = 0; seriesIndex < _seriesCount; seriesIndex++)
            {
                _chartSignals.Add(CreateChartSignal(view, seriesIndex));
            }

            UpdateChartHostHeight();
            UpdateImportButtons();
            ConfigureChartDataSourceBehavior(view, true);
            view.XAxes[0].SetRange(0, 100);

            _chart.EndUpdate();

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void RebuildChartForImportedSignalCount(int signalCount)
        {
            _seriesCount = Math.Max(1, signalCount);
            Start();
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

        private void ShowDataProducerDialog()
        {
            SerialPortDataProducer producer = new SerialPortDataProducer();

            Window dialog = new Window
            {
                Title = "Generate BIN",
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
            SignalImportSelection selection = null;

            Window dialog = new Window
            {
                Title = "Import",
                Owner = this,
                Width = 560,
                Height = 310,
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
            protocolComboBox.Items.Add("Single channel");
            protocolComboBox.Items.Add("2-wire");
            protocolComboBox.Items.Add("3-wire");
            protocolComboBox.Items.Add("4-wire");
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

            TextBlock sampleRateLabel = new TextBlock
            {
                Text = "Sample Rate:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 10, 0)
            };
            Grid.SetRow(sampleRateLabel, 2);
            Grid.SetColumn(sampleRateLabel, 0);
            layoutRoot.Children.Add(sampleRateLabel);

            TextBox sampleRateTextBox = new TextBox
            {
                Margin = new Thickness(0, 12, 10, 0),
                MinWidth = 220,
                Text = "50000000"
            };
            Grid.SetRow(sampleRateTextBox, 2);
            Grid.SetColumn(sampleRateTextBox, 1);
            layoutRoot.Children.Add(sampleRateTextBox);

            TextBlock sampleRateUnitTextBlock = new TextBlock
            {
                Text = "Hz",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(sampleRateUnitTextBlock, 2);
            Grid.SetColumn(sampleRateUnitTextBlock, 2);
            layoutRoot.Children.Add(sampleRateUnitTextBlock);

            TextBlock hintTextBlock = new TextBlock
            {
                Text = "导入会根据协议拆分二进制数据，并按采样率换算到时间轴。Single channel 直接导入当前通道。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(hintTextBlock, 3);
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
                uint sampleRate;

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

                if (uint.TryParse(sampleRateTextBox.Text, out sampleRate) == false || sampleRate == 0)
                {
                    MessageBox.Show(dialog, "Sample rate must be a positive integer.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                selection = new SignalImportSelection
                {
                    ProtocolType = GetSelectedImportProtocolType(protocolComboBox.SelectedIndex),
                    ProtocolName = selectedProtocol,
                    FilePath = filePath,
                    SampleRate = sampleRate
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
            Grid.SetRow(buttonBar, 4);
            Grid.SetColumnSpan(buttonBar, 3);
            layoutRoot.Children.Add(buttonBar);

            dialog.Content = layoutRoot;

            bool? result = dialog.ShowDialog();
            if (result != true || selection == null)
            {
                return;
            }

            if (selection.ProtocolType == SerialProtocolType.Uart)
            {
                ImportBinaryWaveformToSignal(signalIndex, selection);
                return;
            }

            ImportProtocolToSignals(signalIndex, selection);
        }

        private void ImportBinaryWaveformToSignal(int signalIndex, SignalImportSelection selection)
        {
            if (_chart == null || selection == null || signalIndex < 0 || signalIndex >= _chartSignals.Count)
            {
                return;
            }

            double sampleInterval = MicrosecondsPerSecond / selection.SampleRate;
            BinaryWaveformImportResult importResult = BinaryWaveformImporter.ImportFile(selection.FilePath, sampleInterval);
            if (importResult == null || importResult.SampleCount <= 0)
            {
                MessageBox.Show(this, "Failed to import waveform from the selected file.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RebuildChartForImportedSignalCount(1);

            _chart.BeginUpdate();
            try
            {
                ConfigureChartDataSourceBehavior(_chart.ViewXY, true);
                ImportWaveformToSignal(0, importResult, importResult.SignalName);

                _lastConsumedX = importResult.SampleCount * importResult.SampleInterval;
                _chart.ViewXY.XAxes[0].SetRange(0, Math.Max(sampleInterval, _lastConsumedX));
            }
            finally
            {
                _chart.EndUpdate();
            }

            InvalidateOverlayCaches();
            UpdateDecodeOverlay();
            UpdateMeasurementVisual();
        }

        private void ImportWaveformToSignal(int signalIndex, BinaryWaveformImportResult importResult, string displayName)
        {
            if (signalIndex < 0
                || signalIndex >= _chartSignals.Count
                || importResult == null
                || importResult.SampleCount <= 0
                || importResult.DigitalWords == null
                || importResult.SampleInterval <= 0)
            {
                return;
            }

            ChartSignal signal = _chartSignals[signalIndex];
            _decodeCache.Remove(signal);
            _measurementCache.Remove(signal);
            signal.ClearDigitalHistory();
            signal.Series.Clear();
            signal.Name = displayName;
            signal.AxisY.Title.Text = displayName;
            signal.Series.FirstSampleTimeStamp = 0;
            signal.Series.SamplingFrequency = 1.0 / importResult.SampleInterval;
            signal.Series.AddBits(importResult.DigitalWords, false);
            signal.SetDigitalHistory(importResult.DigitalWords, importResult.SampleCount, importResult.SampleInterval);
        }

        private void ImportProtocolToSignals(int signalIndex, SignalImportSelection selection)
        {
            if (_chart == null || selection == null)
            {
                return;
            }

            string[] channelNames = GetProtocolChannelNames(selection.ProtocolType);
            if (channelNames == null || channelNames.Length == 0)
            {
                MessageBox.Show(this, "Unsupported import protocol.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[] protocolBytes = File.ReadAllBytes(selection.FilePath);
            if (protocolBytes == null || protocolBytes.Length < channelNames.Length)
            {
                MessageBox.Show(this, "The import file does not contain enough data for the selected protocol.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[][] splitBytes = SplitProtocolBytes(protocolBytes, channelNames.Length);
            if (splitBytes == null || splitBytes.Length != channelNames.Length || splitBytes.Any(bytes => bytes == null || bytes.Length == 0))
            {
                MessageBox.Show(this, "Failed to split the import data into protocol channels.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double sampleInterval = MicrosecondsPerSecond / selection.SampleRate;
            BinaryWaveformImportResult[] importResults = new BinaryWaveformImportResult[channelNames.Length];
            for (int i = 0; i < channelNames.Length; i++)
            {
                importResults[i] = BinaryWaveformImporter.ImportBytes(channelNames[i], splitBytes[i], sampleInterval);
            }

            if (importResults.Any(result => result == null || result.SampleCount <= 0))
            {
                MessageBox.Show(this, "Failed to convert imported protocol data into waveforms.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RebuildChartForImportedSignalCount(channelNames.Length);

            _chart.BeginUpdate();
            try
            {
                ConfigureChartDataSourceBehavior(_chart.ViewXY, true);
                for (int i = 0; i < channelNames.Length; i++)
                {
                    ImportWaveformToSignal(i, importResults[i], channelNames[i]);
                    ApplyImportedProtocolDecodeSettings(_chartSignals[i]);
                }

                _lastConsumedX = importResults.Max(result => result.SampleCount * result.SampleInterval);
                _chart.ViewXY.XAxes[0].SetRange(0, Math.Max(sampleInterval, _lastConsumedX));
            }
            finally
            {
                _chart.EndUpdate();
            }

            InvalidateOverlayCaches();
            UpdateDecodeOverlay();
            UpdateMeasurementVisual();
        }

        private static byte[][] SplitProtocolBytes(byte[] protocolBytes, int channelCount)
        {
            if (protocolBytes == null || protocolBytes.Length < channelCount || channelCount <= 0)
            {
                return null;
            }

            int frameCount = protocolBytes.Length / channelCount;
            byte[][] channels = new byte[channelCount][];
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                channels[channelIndex] = new byte[frameCount];
            }

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                int sourceIndex = frameIndex * channelCount;
                for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    channels[channelIndex][frameIndex] = protocolBytes[sourceIndex + channelIndex];
                }
            }

            return channels;
        }

        private static void ApplyImportedProtocolDecodeSettings(ChartSignal signal)
        {
            if (signal == null || signal.DecodeSettings == null)
            {
                return;
            }

            signal.DecodeSettings.Mode = SignalDecodeMode.FixedWidth8Bit;
            signal.DecodeSettings.DataBits = 8;
        }

        private static SerialProtocolType GetSelectedImportProtocolType(int selectedIndex)
        {
            switch (selectedIndex)
            {
                case 1:
                    return SerialProtocolType.TwoWireSerial;
                case 2:
                    return SerialProtocolType.ThreeWireSerial;
                case 3:
                    return SerialProtocolType.FourWireSerial;
                default:
                    return SerialProtocolType.Uart;
            }
        }

        private static string[] GetProtocolChannelNames(SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return new[] { "CLK", "DATA" };
                case SerialProtocolType.ThreeWireSerial:
                    return new[] { "CLK", "EN", "DATA" };
                case SerialProtocolType.FourWireSerial:
                    return new[] { "CLK", "EN", "DATA0", "DATA1" };
                default:
                    return null;
            }
        }

        private static string GetImportProtocolDisplayName(SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return "2-wire";
                case SerialProtocolType.ThreeWireSerial:
                    return "3-wire";
                case SerialProtocolType.FourWireSerial:
                    return "4-wire";
                default:
                    return "Single channel";
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
                    item.Dispose();
                }
            }
        }

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

        public void Stop()
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isMeasurementHovering = false;
            _measurementHoverXValue = 0;
            _measurementSignal = null;
        }
    }
}
