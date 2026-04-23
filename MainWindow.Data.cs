using Arction.Wpf.Charting;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using LCWpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace InteractiveExamples
{
    public partial class Example8BillionPoints
    {
        private const int ProtocolImportChunkSeconds = 5;
        private const double ProtocolImportPartitionDurationSeconds = 0.1;
        private const int ProtocolImportPartitionsPerChunk = (int)(ProtocolImportChunkSeconds / ProtocolImportPartitionDurationSeconds);

        private sealed class SignalImportSelection
        {
            public SerialProtocolType ProtocolType { get; set; }
            public string ProtocolName { get; set; }
            public string ImportPath { get; set; }
            public uint SampleRate { get; set; }
            public uint DataRate { get; set; }
            public string TimestampText { get; set; }
            public bool HasUartMetadata { get; set; }
            public int UartBaudRate { get; set; }
            public UartParityMode UartParityMode { get; set; }
            public int UartDataBits { get; set; }
            public double UartStopBits { get; set; }
            public int UartSamplesPerBit { get; set; }
        }

        private sealed class ProtocolImportPageItem
        {
            public int PageNumber { get; set; }
            public string DisplayName { get; set; }
            public string FilePath { get; set; }
            public int FilePageNumber { get; set; }
            public int PartitionNumber { get; set; }
            public long OffsetBytes { get; set; }
            public int ByteCount { get; set; }
            public uint SampleRate { get; set; }
            public uint DataRate { get; set; }
            public bool IsActivePartition { get; set; }

            public Brush DisplayBrush
            {
                get
                {
                    return IsActivePartition ? Brushes.LimeGreen : Brushes.DimGray;
                }
            }

            public FontWeight DisplayFontWeight
            {
                get
                {
                    return IsActivePartition ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }

        private sealed class ProtocolImportSession
        {
            public SerialProtocolType ProtocolType { get; set; }
            public string ProtocolName { get; set; }
            public string FolderPath { get; set; }
            public uint SampleRate { get; set; }
            public uint DataRate { get; set; }
            public string TimestampText { get; set; }
            public bool HasUartMetadata { get; set; }
            public int UartBaudRate { get; set; }
            public UartParityMode UartParityMode { get; set; }
            public int UartDataBits { get; set; }
            public double UartStopBits { get; set; }
            public int UartSamplesPerBit { get; set; }
            public List<ProtocolImportPageItem> Pages { get; set; }
        }

        private sealed class ProtocolFileMetadata
        {
            public int LineCount { get; set; }
            public uint SampleRate { get; set; }
            public int FilePageNumber { get; set; }
            public HashSet<int> ActivePartitions { get; set; }
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
            BinFileDataProducer producer = new BinFileDataProducer();

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

        private static UIElement BuildDataProducerDialogContent(UIElement producer)
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
                Height = 390,
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

            TextBlock pathLabel = new TextBlock
            {
                Text = "Folder:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 10, 0)
            };
            Grid.SetRow(pathLabel, 1);
            Grid.SetColumn(pathLabel, 0);
            layoutRoot.Children.Add(pathLabel);

            TextBox importPathTextBox = new TextBox
            {
                Margin = new Thickness(0, 12, 10, 0),
                MinWidth = 220
            };
            Grid.SetRow(importPathTextBox, 1);
            Grid.SetColumn(importPathTextBox, 1);
            layoutRoot.Children.Add(importPathTextBox);

            Button browseButton = new Button
            {
                Content = "Browse...",
                Width = 96,
                Height = 28,
                Margin = new Thickness(0, 12, 0, 0)
            };
            browseButton.Click += (sender, e) =>
            {
                using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
                {
                    folderDialog.Description = "Select a folder that contains the paged BIN files.";
                    folderDialog.ShowNewFolderButton = false;
                    if (folderDialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        importPathTextBox.Text = folderDialog.SelectedPath;
                    }
                }
            };
            Grid.SetRow(browseButton, 1);
            Grid.SetColumn(browseButton, 2);
            layoutRoot.Children.Add(browseButton);

            Action updateImportPathPrompt = () =>
            {
                pathLabel.Text = "Folder:";
            };
            protocolComboBox.SelectionChanged += (sender, e) => updateImportPathPrompt();
            updateImportPathPrompt();

            TextBlock sampleRateLabel = new TextBlock
            {
                Text = "Sample Rate:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 10, 0)
            };
            Grid.SetRow(sampleRateLabel, 2);
            Grid.SetColumn(sampleRateLabel, 0);
            sampleRateLabel.Visibility = Visibility.Collapsed;
            layoutRoot.Children.Add(sampleRateLabel);

            TextBox sampleRateTextBox = new TextBox
            {
                Margin = new Thickness(0, 12, 10, 0),
                MinWidth = 220,
                Text = "50000000"
            };
            Grid.SetRow(sampleRateTextBox, 2);
            Grid.SetColumn(sampleRateTextBox, 1);
            sampleRateTextBox.Visibility = Visibility.Collapsed;
            layoutRoot.Children.Add(sampleRateTextBox);

            TextBlock sampleRateUnitTextBlock = new TextBlock
            {
                Text = "Hz",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(sampleRateUnitTextBlock, 2);
            Grid.SetColumn(sampleRateUnitTextBlock, 2);
            sampleRateUnitTextBlock.Visibility = Visibility.Collapsed;
            layoutRoot.Children.Add(sampleRateUnitTextBlock);

            TextBlock dataRateLabel = new TextBlock
            {
                Text = "Data Rate:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 10, 0)
            };
            Grid.SetRow(dataRateLabel, 3);
            Grid.SetColumn(dataRateLabel, 0);
            layoutRoot.Children.Add(dataRateLabel);

            TextBox dataRateTextBox = new TextBox
            {
                Margin = new Thickness(0, 12, 10, 0),
                MinWidth = 220,
                Text = "5M"
            };
            Grid.SetRow(dataRateTextBox, 3);
            Grid.SetColumn(dataRateTextBox, 1);
            layoutRoot.Children.Add(dataRateTextBox);

            TextBlock dataRateUnitTextBlock = new TextBlock
            {
                Text = "bps",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(dataRateUnitTextBlock, 3);
            Grid.SetColumn(dataRateUnitTextBlock, 2);
            layoutRoot.Children.Add(dataRateUnitTextBlock);

            TextBlock folderMetadataTextBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = Brushes.DimGray,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(folderMetadataTextBlock, 4);
            Grid.SetColumnSpan(folderMetadataTextBlock, 3);
            layoutRoot.Children.Add(folderMetadataTextBlock);

            Action updateDataRateVisibility = () =>
            {
                Visibility visibility = GetSelectedImportProtocolType(protocolComboBox.SelectedIndex) == SerialProtocolType.Uart
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                dataRateLabel.Visibility = visibility;
                dataRateTextBox.Visibility = visibility;
                dataRateUnitTextBlock.Visibility = visibility;
            };
            Action updateProtocolFolderMetadata = () =>
            {
                SerialProtocolType selectedProtocolType = GetSelectedImportProtocolType(protocolComboBox.SelectedIndex);
                dataRateTextBox.IsEnabled = false;
                folderMetadataTextBlock.Visibility = Visibility.Visible;

                string folderPath = importPathTextBox.Text == null ? string.Empty : importPathTextBox.Text.Trim();
                if (selectedProtocolType == SerialProtocolType.Uart)
                {
                    UartBinFileMetadata uartMetadata;
                    if (string.IsNullOrWhiteSpace(folderPath) == false
                        && ProtocolBinNaming.TryParseUartFolderMetadata(folderPath, out uartMetadata)
                        && uartMetadata.LineCount == 1)
                    {
                        sampleRateTextBox.Text = uartMetadata.SampleRate.ToString(CultureInfo.InvariantCulture);
                        dataRateTextBox.Text = string.Empty;
                        folderMetadataTextBlock.Foreground = Brushes.DimGray;
                        folderMetadataTextBlock.Text = string.Format(
                            CultureInfo.InvariantCulture,
                            "UART folder metadata: Sample Rate {0} Hz, Baud {1} bps, Parity {2}, Data Bits {3}, Stop Bits {4}, Time {5}",
                            uartMetadata.SampleRate,
                            uartMetadata.BaudRate,
                            uartMetadata.ParityText,
                            uartMetadata.DataBits,
                            uartMetadata.StopBits,
                            uartMetadata.TimestampText);
                        return;
                    }

                    sampleRateTextBox.Text = string.Empty;
                    dataRateTextBox.Text = string.Empty;
                    folderMetadataTextBlock.Foreground = Brushes.DarkRed;
                    folderMetadataTextBlock.Text = "Folder name must be: 1;sampleRate;baudRate;parity;dataBits;stopBits;timestamp for single channel imports.";
                    return;
                }

                ProtocolBinFolderMetadata folderMetadata;
                if (string.IsNullOrWhiteSpace(folderPath) == false
                    && ProtocolBinNaming.TryParseFolderMetadata(folderPath, out folderMetadata)
                    && folderMetadata.DataRate > 0)
                {
                    sampleRateTextBox.Text = folderMetadata.SampleRate.ToString(CultureInfo.InvariantCulture);
                    dataRateTextBox.Text = folderMetadata.DataRate.ToString(CultureInfo.InvariantCulture);
                    folderMetadataTextBlock.Foreground = Brushes.DimGray;
                    folderMetadataTextBlock.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "Folder metadata: Sample Rate {0} Hz, Data Rate {1} bps, Time {2}",
                        folderMetadata.SampleRate,
                        folderMetadata.DataRate,
                        folderMetadata.TimestampText);
                }
                else
                {
                    sampleRateTextBox.Text = string.Empty;
                    dataRateTextBox.Text = string.Empty;
                    folderMetadataTextBlock.Foreground = Brushes.DarkRed;
                    folderMetadataTextBlock.Text = "Folder name must be: lineCount;sampleRate;dataRate;timestamp; for 2-wire / 3-wire / 4-wire imports.";
                }
            };
            protocolComboBox.SelectionChanged += (sender, e) =>
            {
                updateDataRateVisibility();
                updateProtocolFolderMetadata();
            };
            importPathTextBox.TextChanged += (sender, e) => updateProtocolFolderMetadata();
            updateDataRateVisibility();
            updateProtocolFolderMetadata();

            TextBlock hintTextBlock = new TextBlock
            {
                Text = "Single channel imports a folder named 1;sampleRate;baudRate;parity;dataBits;stopBits;timestamp. 2-wire / 3-wire / 4-wire imports select a folder named lineCount;sampleRate;dataRate;timestamp;. Sample rate is read from the folder name.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(hintTextBlock, 5);
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
                SerialProtocolType selectedProtocolType = GetSelectedImportProtocolType(protocolComboBox.SelectedIndex);
                string importPath = importPathTextBox.Text == null ? string.Empty : importPathTextBox.Text.Trim();
                uint sampleRate = 0;
                uint dataRate = 0;
                string timestampText = string.Empty;
                bool hasUartMetadata = false;
                int uartBaudRate = 0;
                UartParityMode uartParityMode = UartParityMode.None;
                int uartDataBits = 0;
                double uartStopBits = 0;
                int uartSamplesPerBit = 0;

                if (string.IsNullOrEmpty(selectedProtocol))
                {
                    MessageBox.Show(dialog, "Please select a protocol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(importPath))
                {
                    MessageBox.Show(dialog, "Please select a folder.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (Directory.Exists(importPath) == false)
                {
                    MessageBox.Show(dialog, "Selected folder does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedProtocolType == SerialProtocolType.Uart)
                {
                    UartBinFileMetadata uartMetadata;
                    if (ProtocolBinNaming.TryParseUartFolderMetadata(importPath, out uartMetadata) == false)
                    {
                        MessageBox.Show(dialog, "Folder name must be: 1;sampleRate;baudRate;parity;dataBits;stopBits;timestamp.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (uartMetadata.LineCount != 1)
                    {
                        MessageBox.Show(dialog, "Single channel folder line count must be 1.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (TryParseUartParityMode(uartMetadata.ParityText, out uartParityMode) == false)
                    {
                        MessageBox.Show(dialog, "UART parity in the folder name is invalid.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    sampleRate = uartMetadata.SampleRate;
                    timestampText = uartMetadata.TimestampText;
                    hasUartMetadata = true;
                    uartBaudRate = uartMetadata.BaudRate;
                    uartDataBits = uartMetadata.DataBits;
                    uartStopBits = uartMetadata.StopBits;
                    uartSamplesPerBit = GetSamplesPerBit(uartMetadata.SampleRate, (uint)uartMetadata.BaudRate);
                    if (sampleRate == 0 || uartSamplesPerBit <= 0)
                    {
                        MessageBox.Show(dialog, "UART sample rate and repeated samples per bit must be positive values.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    ProtocolBinFolderMetadata folderMetadata;
                    if (ProtocolBinNaming.TryParseFolderMetadata(importPath, out folderMetadata) == false || folderMetadata.DataRate == 0)
                    {
                        MessageBox.Show(dialog, "Folder name must be: lineCount;sampleRate;dataRate;timestamp;.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string[] channelNames = GetProtocolChannelNames(selectedProtocolType);
                    int expectedLineCount = channelNames == null ? 0 : channelNames.Length;
                    if (expectedLineCount > 0 && folderMetadata.LineCount != expectedLineCount)
                    {
                        MessageBox.Show(dialog, "Folder line count does not match the selected protocol.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    sampleRate = folderMetadata.SampleRate;
                    dataRate = folderMetadata.DataRate;
                    timestampText = folderMetadata.TimestampText;
                    if (sampleRate == 0 || dataRate == 0)
                    {
                        MessageBox.Show(dialog, "Folder sample rate and data rate must be positive values.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                selection = new SignalImportSelection
                {
                    ProtocolType = selectedProtocolType,
                    ProtocolName = selectedProtocol,
                    ImportPath = importPath,
                    SampleRate = sampleRate,
                    DataRate = dataRate,
                    TimestampText = timestampText,
                    HasUartMetadata = hasUartMetadata,
                    UartBaudRate = uartBaudRate,
                    UartParityMode = uartParityMode,
                    UartDataBits = uartDataBits,
                    UartStopBits = uartStopBits,
                    UartSamplesPerBit = uartSamplesPerBit
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
            Grid.SetRow(buttonBar, 6);
            Grid.SetColumnSpan(buttonBar, 3);
            layoutRoot.Children.Add(buttonBar);

            dialog.Content = layoutRoot;

            bool? result = dialog.ShowDialog();
            if (result != true || selection == null)
            {
                return;
            }

            ImportProtocolFolderToSignals(signalIndex, selection);
        }

        private void ImportBinaryWaveformToSignal(int signalIndex, SignalImportSelection selection)
        {
            if (_chart == null || selection == null || signalIndex < 0 || signalIndex >= _chartSignals.Count)
            {
                return;
            }

            double sampleInterval = MicrosecondsPerSecond / selection.SampleRate;
            BinaryWaveformImportResult importResult = BinaryWaveformImporter.ImportFile(selection.ImportPath, sampleInterval);
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
                if (selection.HasUartMetadata)
                {
                    ApplyImportedUartDecodeSettings(_chartSignals[0], selection);
                }

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

        private void ImportProtocolFolderToSignals(int signalIndex, SignalImportSelection selection)
        {
            if (_chart == null || selection == null)
            {
                return;
            }

            List<ProtocolImportPageItem> pages = GetProtocolImportPages(selection.ImportPath, selection.ProtocolType, selection.SampleRate, selection.DataRate);
            if (pages == null || pages.Count == 0)
            {
                MessageBox.Show(this, "No paged BIN files were found in the selected folder.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentProtocolImportSession = new ProtocolImportSession
            {
                ProtocolType = selection.ProtocolType,
                ProtocolName = selection.ProtocolName,
                FolderPath = selection.ImportPath,
                SampleRate = selection.SampleRate,
                DataRate = selection.DataRate,
                TimestampText = selection.TimestampText,
                HasUartMetadata = selection.HasUartMetadata,
                UartBaudRate = selection.UartBaudRate,
                UartParityMode = selection.UartParityMode,
                UartDataBits = selection.UartDataBits,
                UartStopBits = selection.UartStopBits,
                UartSamplesPerBit = selection.UartSamplesPerBit,
                Pages = pages
            };

            BindProtocolImportPages();
            LoadProtocolImportPage(signalIndex, pages[0]);
        }

        private void LoadProtocolImportPage(int signalIndex, ProtocolImportPageItem page)
        {
            if (_chart == null || _currentProtocolImportSession == null || page == null || signalIndex < 0)
            {
                return;
            }

            string[] channelNames = GetProtocolChannelNames(_currentProtocolImportSession.ProtocolType);
            if (channelNames == null || channelNames.Length == 0)
            {
                MessageBox.Show(this, "Unsupported import protocol.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[] protocolBytes = ReadProtocolPartitionBytes(page);
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

            double sampleInterval = MicrosecondsPerSecond / page.SampleRate;
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

            bool isUartImport = _currentProtocolImportSession.ProtocolType == SerialProtocolType.Uart;
            int samplesPerBit = isUartImport
                ? _currentProtocolImportSession.UartSamplesPerBit
                : GetSamplesPerBit(page.SampleRate, page.DataRate);
            if (samplesPerBit <= 0)
            {
                MessageBox.Show(this, "Repeated samples per bit is invalid.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    if (isUartImport)
                    {
                        ApplyImportedUartDecodeSettings(_chartSignals[i], _currentProtocolImportSession);
                    }
                    else
                    {
                        ApplyImportedProtocolDecodeSettings(_chartSignals[i], samplesPerBit);
                    }
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

        private void BindProtocolImportPages()
        {
            if (comboBoxImportPage == null)
            {
                return;
            }

            _isUpdatingImportPageSelection = true;
            comboBoxImportPage.ItemsSource = null;
            comboBoxImportPage.SelectedItem = null;

            if (_currentProtocolImportSession != null && _currentProtocolImportSession.Pages != null)
            {
                comboBoxImportPage.ItemsSource = _currentProtocolImportSession.Pages;
                comboBoxImportPage.SelectedIndex = _currentProtocolImportSession.Pages.Count > 0 ? 0 : -1;
            }

            _isUpdatingImportPageSelection = false;
            UpdateImportButtons();
        }

        private void ClearProtocolImportSession()
        {
            _currentProtocolImportSession = null;
            if (comboBoxImportPage != null)
            {
                _isUpdatingImportPageSelection = true;
                comboBoxImportPage.ItemsSource = null;
                comboBoxImportPage.SelectedItem = null;
                _isUpdatingImportPageSelection = false;
            }

            UpdateImportButtons();
        }

        private static List<ProtocolImportPageItem> GetProtocolImportPages(string folderPath, SerialProtocolType protocolType, uint fallbackSampleRate, uint dataRate)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || Directory.Exists(folderPath) == false)
            {
                return null;
            }

            string[] filePaths = Directory.GetFiles(folderPath, "*.bin", SearchOption.TopDirectoryOnly);
            if (filePaths == null || filePaths.Length == 0)
            {
                return new List<ProtocolImportPageItem>();
            }

            string[] channelNames = GetProtocolChannelNames(protocolType);
            int expectedLineCount = channelNames == null ? 0 : channelNames.Length;
            ProtocolBinFolderMetadata folderMetadata = null;
            bool hasFolderMetadata;
            if (protocolType == SerialProtocolType.Uart)
            {
                UartBinFileMetadata uartFolderMetadata;
                hasFolderMetadata = ProtocolBinNaming.TryParseUartFolderMetadata(folderPath, out uartFolderMetadata);
                if (hasFolderMetadata)
                {
                    folderMetadata = new ProtocolBinFolderMetadata
                    {
                        LineCount = uartFolderMetadata.LineCount,
                        SampleRate = uartFolderMetadata.SampleRate,
                        DataRate = 0,
                        TimestampText = uartFolderMetadata.TimestampText
                    };
                }
            }
            else
            {
                hasFolderMetadata = ProtocolBinNaming.TryParseFolderMetadata(folderPath, out folderMetadata);
            }
            List<ProtocolImportPageItem> pages = new List<ProtocolImportPageItem>();
            int globalPageNumber = 1;

            foreach (string filePath in filePaths)
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                ProtocolFileMetadata metadata;

                ProtocolBinChunkFileMetadata chunkMetadata;
                if (hasFolderMetadata
                    && ProtocolBinNaming.TryParseChunkFileMetadata(fileNameWithoutExtension, out chunkMetadata))
                {
                    metadata = new ProtocolFileMetadata
                    {
                        LineCount = folderMetadata.LineCount,
                        SampleRate = folderMetadata.SampleRate,
                        FilePageNumber = chunkMetadata.FilePageNumber,
                        ActivePartitions = chunkMetadata.ActivePartitions
                    };
                }
                else if (TryParseLegacyProtocolFileMetadata(fileNameWithoutExtension, out metadata) == false)
                {
                    continue;
                }

                if (expectedLineCount > 0 && metadata.LineCount != expectedLineCount)
                {
                    continue;
                }

                int bytesPerPartition = GetProtocolPartitionByteCount(metadata.LineCount, metadata.SampleRate);
                if (bytesPerPartition <= 0)
                {
                    continue;
                }

                long fileLength = new FileInfo(filePath).Length;
                int partitionCount = (int)(fileLength / bytesPerPartition);
                if (partitionCount <= 0)
                {
                    continue;
                }

                for (int partitionNumber = 1; partitionNumber <= partitionCount; partitionNumber++)
                {
                    pages.Add(new ProtocolImportPageItem
                    {
                        PageNumber = globalPageNumber,
                        DisplayName = string.Format(
                            CultureInfo.InvariantCulture,
                            "Page {0} (File {1}, Part {2})",
                            globalPageNumber,
                            metadata.FilePageNumber,
                            partitionNumber),
                        FilePath = filePath,
                        FilePageNumber = metadata.FilePageNumber,
                        PartitionNumber = partitionNumber,
                        OffsetBytes = (long)(partitionNumber - 1) * bytesPerPartition,
                        ByteCount = bytesPerPartition,
                        SampleRate = metadata.SampleRate,
                        DataRate = dataRate,
                        IsActivePartition = metadata.ActivePartitions != null && metadata.ActivePartitions.Contains(partitionNumber)
                    });
                    globalPageNumber++;
                }
            }

            if (pages.Count == 0)
            {
                uint effectiveFallbackSampleRate = hasFolderMetadata ? folderMetadata.SampleRate : fallbackSampleRate;
                int fallbackFilePageNumber = 1;
                foreach (string filePath in filePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    long fileLength = new FileInfo(filePath).Length;
                    ProtocolBinChunkFileMetadata chunkMetadata;
                    int parsedFilePageNumber = ProtocolBinNaming.TryParseChunkFileMetadata(
                        Path.GetFileNameWithoutExtension(filePath),
                        out chunkMetadata)
                        ? chunkMetadata.FilePageNumber
                        : fallbackFilePageNumber;
                    int fallbackLineCount = hasFolderMetadata && folderMetadata.LineCount > 0
                        ? folderMetadata.LineCount
                        : expectedLineCount;
                    int bytesPerPartition = GetProtocolPartitionByteCount(fallbackLineCount, effectiveFallbackSampleRate);
                    int partitionCount = bytesPerPartition <= 0 ? 0 : Math.Max(1, (int)(fileLength / bytesPerPartition));

                    for (int partitionNumber = 1; partitionNumber <= partitionCount; partitionNumber++)
                    {
                        pages.Add(new ProtocolImportPageItem
                        {
                            PageNumber = globalPageNumber,
                            DisplayName = string.Format(
                                CultureInfo.InvariantCulture,
                                "Page {0} (File {1}, Part {2})",
                                globalPageNumber,
                                parsedFilePageNumber,
                                partitionNumber),
                            FilePath = filePath,
                            FilePageNumber = parsedFilePageNumber,
                            PartitionNumber = partitionNumber,
                            OffsetBytes = (long)(partitionNumber - 1) * bytesPerPartition,
                            ByteCount = bytesPerPartition,
                            SampleRate = effectiveFallbackSampleRate,
                            DataRate = dataRate,
                            IsActivePartition = chunkMetadata != null && chunkMetadata.ActivePartitions != null && chunkMetadata.ActivePartitions.Contains(partitionNumber)
                        });
                        globalPageNumber++;
                    }

                    fallbackFilePageNumber++;
                }
            }

            List<ProtocolImportPageItem> orderedPages = pages
                .OrderBy(page => page.FilePageNumber)
                .ThenBy(page => page.PartitionNumber)
                .ThenBy(page => page.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < orderedPages.Count; i++)
            {
                orderedPages[i].PageNumber = i + 1;
                orderedPages[i].DisplayName = string.Format(
                    CultureInfo.InvariantCulture,
                    "Page {0} (File {1}, Part {2})",
                    i + 1,
                    orderedPages[i].FilePageNumber,
                    orderedPages[i].PartitionNumber);
            }

            return orderedPages;
        }

        private static byte[] ReadProtocolPartitionBytes(ProtocolImportPageItem page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.FilePath) || File.Exists(page.FilePath) == false || page.ByteCount <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[page.ByteCount];
            using (FileStream stream = new FileStream(page.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (page.OffsetBytes < 0 || page.OffsetBytes >= stream.Length)
                {
                    return null;
                }

                stream.Seek(page.OffsetBytes, SeekOrigin.Begin);
                int totalBytesRead = 0;
                while (totalBytesRead < buffer.Length)
                {
                    int bytesRead = stream.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead != buffer.Length)
                {
                    return null;
                }
            }

            return buffer;
        }

        private static bool TryParseLegacyProtocolFileMetadata(string fileNameWithoutExtension, out ProtocolFileMetadata metadata)
        {
            metadata = null;
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return false;
            }

            string[] parts = fileNameWithoutExtension.Split(';');
            if (parts.Length < 4)
            {
                return false;
            }

            int lineCount;
            uint sampleRate;
            int filePageNumber;
            HashSet<int> activePartitions;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out lineCount) == false
                || uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate) == false
                || TryParseProtocolPageSuffix(parts[parts.Length - 1], out activePartitions, out filePageNumber) == false)
            {
                return false;
            }

            metadata = new ProtocolFileMetadata
            {
                LineCount = lineCount,
                SampleRate = sampleRate,
                FilePageNumber = filePageNumber,
                ActivePartitions = activePartitions
            };
            return true;
        }

        private static bool TryParseProtocolPageSuffix(string value, out HashSet<int> activePartitions, out int pageNumber)
        {
            activePartitions = new HashSet<int>();
            pageNumber = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int separatorIndex = value.LastIndexOf('-');
            if (separatorIndex < 0 || separatorIndex >= value.Length - 1)
            {
                return false;
            }

            if (int.TryParse(value.Substring(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out pageNumber) == false
                || pageNumber <= 0)
            {
                return false;
            }

            string activePartitionList = value.Substring(0, separatorIndex);
            if (string.IsNullOrWhiteSpace(activePartitionList))
            {
                return true;
            }

            string[] partitionTokens = activePartitionList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in partitionTokens)
            {
                int partitionNumber;
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out partitionNumber) && partitionNumber > 0)
                {
                    activePartitions.Add(partitionNumber);
                }
            }

            return true;
        }

        private static int GetProtocolPartitionByteCount(int lineCount, uint sampleRate)
        {
            if (lineCount <= 0 || sampleRate == 0)
            {
                return 0;
            }

            long bytesPerSecond = (long)Math.Round((sampleRate * lineCount) / 8.0, MidpointRounding.AwayFromZero);
            long totalBytes = (long)Math.Round(bytesPerSecond * ProtocolImportPartitionDurationSeconds, MidpointRounding.AwayFromZero);
            if (totalBytes <= 0 || totalBytes > int.MaxValue)
            {
                return 0;
            }

            return (int)totalBytes;
        }

        private static int GetSamplesPerBit(uint sampleRate, uint dataRate)
        {
            if (sampleRate == 0 || dataRate == 0)
            {
                return 0;
            }

            double samplesPerBit = Math.Round(sampleRate / (double)dataRate, MidpointRounding.AwayFromZero);
            return samplesPerBit <= 0 || samplesPerBit > int.MaxValue ? 0 : (int)samplesPerBit;
        }

        private static bool TryParseFrequency(string text, out uint frequency)
        {
            frequency = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalizedText = text.Trim().ToUpperInvariant();
            double multiplier = 1;
            char suffix = normalizedText[normalizedText.Length - 1];
            if (suffix == 'K' || suffix == 'M' || suffix == 'G')
            {
                normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
                switch (suffix)
                {
                    case 'K':
                        multiplier = 1000;
                        break;
                    case 'M':
                        multiplier = 1000000;
                        break;
                    case 'G':
                        multiplier = 1000000000;
                        break;
                }
            }

            double baseValue;
            if (double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out baseValue) == false
                && double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.CurrentCulture, out baseValue) == false)
            {
                return false;
            }

            double scaledValue = baseValue * multiplier;
            if (scaledValue <= 0 || scaledValue > uint.MaxValue)
            {
                return false;
            }

            frequency = (uint)Math.Round(scaledValue, MidpointRounding.AwayFromZero);
            return frequency > 0;
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

        private static void ApplyImportedProtocolDecodeSettings(ChartSignal signal, int samplesPerBit)
        {
            if (signal == null || signal.DecodeSettings == null)
            {
                return;
            }

            signal.DecodeSettings.Mode = SignalDecodeMode.FixedWidth8Bit;
            signal.DecodeSettings.DataBits = 8;
            signal.DecodeSettings.SamplesPerBit = samplesPerBit;
        }

        private static void ApplyImportedUartDecodeSettings(ChartSignal signal, SignalImportSelection selection)
        {
            if (signal == null || signal.DecodeSettings == null || selection == null || selection.HasUartMetadata == false)
            {
                return;
            }

            signal.DecodeSettings.Mode = SignalDecodeMode.UartFrame;
            signal.DecodeSettings.BaudRate = selection.UartBaudRate;
            signal.DecodeSettings.ParityMode = selection.UartParityMode;
            signal.DecodeSettings.DataBits = selection.UartDataBits;
            signal.DecodeSettings.StopBits = selection.UartStopBits;
            signal.DecodeSettings.IdleBits = 1;
            signal.DecodeSettings.SamplesPerBit = selection.UartSamplesPerBit;
        }

        private static void ApplyImportedUartDecodeSettings(ChartSignal signal, ProtocolImportSession session)
        {
            if (signal == null || signal.DecodeSettings == null || session == null || session.HasUartMetadata == false)
            {
                return;
            }

            signal.DecodeSettings.Mode = SignalDecodeMode.UartFrame;
            signal.DecodeSettings.BaudRate = session.UartBaudRate;
            signal.DecodeSettings.ParityMode = session.UartParityMode;
            signal.DecodeSettings.DataBits = session.UartDataBits;
            signal.DecodeSettings.StopBits = session.UartStopBits;
            signal.DecodeSettings.IdleBits = 1;
            signal.DecodeSettings.SamplesPerBit = session.UartSamplesPerBit;
        }

        private static bool TryParseUartParityMode(string text, out UartParityMode parityMode)
        {
            parityMode = UartParityMode.None;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            switch (text.Trim())
            {
                case "None":
                    parityMode = UartParityMode.None;
                    return true;
                case "Odd":
                    parityMode = UartParityMode.Odd;
                    return true;
                case "Even":
                    parityMode = UartParityMode.Even;
                    return true;
                case "Mark":
                    parityMode = UartParityMode.Mark;
                    return true;
                case "Space":
                    parityMode = UartParityMode.Space;
                    return true;
                default:
                    return false;
            }
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
                case SerialProtocolType.Uart:
                    return new[] { "UART" };
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
