using Arction.Wpf.Charting;
using Arction.Wpf.Charting.SeriesXY;
using Arction.Wpf.Charting.Views.ViewXY;
using ScopeAnalysis;
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

namespace ScopeAnalysis
{
    public partial class MainWindow
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
            public ProtocolBitOrder BitOrder { get; set; }
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
            public int ChannelCount { get; set; }
            public int BytesPerChannel { get; set; }
            public int SampleCount { get; set; }
            public long StartSampleIndex { get; set; }
            public int LeadingHighSamples { get; set; }

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
            public ProtocolBitOrder BitOrder { get; set; }
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
                Title = "生成 BIN",
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

        private void ShowCollectionDialog()
        {
            CollectionDataProducer producer = new CollectionDataProducer();

            Window dialog = new Window
            {
                Title = "采集",
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
                Content = "关闭",
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

            using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "选择包含 BIN 文件的文件夹。";
                folderDialog.ShowNewFolderButton = false;
                folderDialog.SelectedPath = _currentProtocolImportSession == null
                    ? string.Empty
                    : _currentProtocolImportSession.FolderPath;

                if (folderDialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }

                SignalImportSelection selection;
                string validationMessage;
                if (TryBuildSignalImportSelectionFromFolder(folderDialog.SelectedPath, out selection, out validationMessage) == false)
                {
                    MessageBox.Show(this, validationMessage, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ImportProtocolFolderToSignals(signalIndex, selection);
            }
        }

        private static bool TryBuildSignalImportSelectionFromFolder(
            string importPath,
            out SignalImportSelection selection,
            out string validationMessage)
        {
            selection = null;
            validationMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(importPath))
            {
                validationMessage = "请选择文件夹。";
                return false;
            }

            if (Directory.Exists(importPath) == false)
            {
                validationMessage = "所选文件夹不存在。";
                return false;
            }

            ProtocolPageManifest manifest;
            if (ProtocolPageManifestStorage.TryLoad(importPath, out manifest))
            {
                SerialProtocolType manifestProtocolType;
                if (TryParseManifestProtocolType(manifest.ProtocolType, out manifestProtocolType) == false)
                {
                    validationMessage = "manifest.json 中的 protocolType 无效。";
                    return false;
                }

                if (manifest.LineCount <= 0 || manifest.SampleRate == 0 || manifest.Pages == null || manifest.Pages.Count == 0)
                {
                    validationMessage = "manifest.json 缺少必要的分页元数据。";
                    return false;
                }

                if (manifestProtocolType == SerialProtocolType.Uart)
                {
                    UartParityMode uartParityMode;
                    if (TryParseUartParityMode(manifest.ParityText, out uartParityMode) == false)
                    {
                        validationMessage = "manifest.json 中的串口校验位无效。";
                        return false;
                    }

                    int uartSamplesPerBit = manifest.SamplesPerBit > 0
                        ? manifest.SamplesPerBit
                        : GetSamplesPerBit(manifest.SampleRate, (uint)Math.Max(0, manifest.BaudRate));
                    if (manifest.BaudRate <= 0 || uartSamplesPerBit <= 0 || manifest.DataBits <= 0 || manifest.StopBits <= 0)
                    {
                        validationMessage = "manifest.json 中的串口参数不完整。";
                        return false;
                    }

                    selection = new SignalImportSelection
                    {
                        ProtocolType = SerialProtocolType.Uart,
                        ProtocolName = GetImportProtocolDisplayName(SerialProtocolType.Uart),
                        ImportPath = importPath,
                        SampleRate = manifest.SampleRate,
                        DataRate = 0,
                        TimestampText = manifest.TimestampText,
                        HasUartMetadata = true,
                        UartBaudRate = manifest.BaudRate,
                        UartParityMode = uartParityMode,
                        UartDataBits = manifest.DataBits,
                        UartStopBits = manifest.StopBits,
                        UartSamplesPerBit = uartSamplesPerBit
                    };
                    return true;
                }

                if (manifest.DataRate == 0)
                {
                    validationMessage = "对于固定宽度协议导入，manifest.json 中的 dataRate 必须为正数。";
                    return false;
                }

                ProtocolBitOrder bitOrder;
                if (TryParseProtocolBitOrder(manifest.BitOrder, out bitOrder) == false)
                {
                    validationMessage = "manifest.json 中的 bitOrder 无效。";
                    return false;
                }

                selection = new SignalImportSelection
                {
                    ProtocolType = manifestProtocolType,
                    ProtocolName = GetImportProtocolDisplayName(manifestProtocolType),
                    ImportPath = importPath,
                    SampleRate = manifest.SampleRate,
                    DataRate = manifest.DataRate,
                    BitOrder = bitOrder,
                    TimestampText = manifest.TimestampText,
                    HasUartMetadata = false,
                    UartBaudRate = 0,
                    UartParityMode = UartParityMode.None,
                    UartDataBits = 0,
                    UartStopBits = 0,
                    UartSamplesPerBit = 0
                };
                return true;
            }

            UartBinFileMetadata uartMetadata;
            if (ProtocolBinNaming.TryParseUartFolderMetadata(importPath, out uartMetadata))
            {
                if (uartMetadata.LineCount != 1)
                {
                    validationMessage = "单通道文件夹的线数必须为 1。";
                    return false;
                }

                UartParityMode uartParityMode;
                if (TryParseUartParityMode(uartMetadata.ParityText, out uartParityMode) == false)
                {
                    validationMessage = "文件夹名中的串口校验位无效。";
                    return false;
                }

                int uartSamplesPerBit = GetSamplesPerBit(uartMetadata.SampleRate, (uint)uartMetadata.BaudRate);
                if (uartMetadata.SampleRate == 0 || uartSamplesPerBit <= 0)
                {
                    validationMessage = "串口采样率和每位重复采样点数必须为正数。";
                    return false;
                }

                selection = new SignalImportSelection
                {
                    ProtocolType = SerialProtocolType.Uart,
                    ProtocolName = GetImportProtocolDisplayName(SerialProtocolType.Uart),
                    ImportPath = importPath,
                    SampleRate = uartMetadata.SampleRate,
                    DataRate = 0,
                    TimestampText = uartMetadata.TimestampText,
                    HasUartMetadata = true,
                    UartBaudRate = uartMetadata.BaudRate,
                    UartParityMode = uartParityMode,
                    UartDataBits = uartMetadata.DataBits,
                    UartStopBits = uartMetadata.StopBits,
                    UartSamplesPerBit = uartSamplesPerBit
                };
                return true;
            }

            ProtocolBinFolderMetadata folderMetadata;
            if (ProtocolBinNaming.TryParseFolderMetadata(importPath, out folderMetadata) == false || folderMetadata.DataRate == 0)
            {
                validationMessage = "文件夹名称必须是 1;采样率;波特率;校验位;数据位;停止位;时间戳，或 线数;采样率;数据速率;大小端;时间戳; 这种格式。";
                return false;
            }

            SerialProtocolType protocolType;
            if (TryGetProtocolTypeFromLineCount(folderMetadata.LineCount, out protocolType) == false)
            {
                validationMessage = folderMetadata.LineCount == 1
                    ? "单通道导入要求文件夹名类似 1;采样率;波特率;校验位;数据位;停止位;时间戳。"
                    : "文件夹线数必须为 2、3 或 4，才能自动判断导入类型。";
                return false;
            }

            if (folderMetadata.SampleRate == 0 || folderMetadata.DataRate == 0)
            {
                validationMessage = "文件夹中的采样率和数据速率必须为正数。";
                return false;
            }

            selection = new SignalImportSelection
            {
                ProtocolType = protocolType,
                ProtocolName = GetImportProtocolDisplayName(protocolType),
                ImportPath = importPath,
                SampleRate = folderMetadata.SampleRate,
                DataRate = folderMetadata.DataRate,
                BitOrder = folderMetadata.BitOrder,
                TimestampText = folderMetadata.TimestampText,
                HasUartMetadata = false,
                UartBaudRate = 0,
                UartParityMode = UartParityMode.None,
                UartDataBits = 0,
                UartStopBits = 0,
                UartSamplesPerBit = 0
            };
            return true;
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
                MessageBox.Show(this, "无法从所选文件导入波形。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this, "所选文件夹中未找到分页 BIN 文件。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentProtocolImportSession = new ProtocolImportSession
            {
                ProtocolType = selection.ProtocolType,
                ProtocolName = selection.ProtocolName,
                FolderPath = selection.ImportPath,
                SampleRate = selection.SampleRate,
                DataRate = selection.DataRate,
                BitOrder = selection.BitOrder,
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
                MessageBox.Show(this, "不支持该导入协议。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[] protocolBytes = ReadProtocolPartitionBytes(page);
            if (protocolBytes == null || protocolBytes.Length < channelNames.Length)
            {
                MessageBox.Show(this, "导入文件中的数据不足，无法解析所选协议。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[][] splitBytes = page.BytesPerChannel > 0
                ? SplitProtocolBytes(protocolBytes, channelNames.Length, page.BytesPerChannel)
                : SplitProtocolBytes(protocolBytes, channelNames.Length);
            if (splitBytes == null || splitBytes.Length != channelNames.Length || splitBytes.Any(bytes => bytes == null || bytes.Length == 0))
            {
                MessageBox.Show(this, "无法将导入数据拆分为对应的协议通道。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double sampleInterval = MicrosecondsPerSecond / page.SampleRate;
            BinaryWaveformImportResult[] importResults = new BinaryWaveformImportResult[channelNames.Length];
            for (int i = 0; i < channelNames.Length; i++)
            {
                int exactSampleCount = page.SampleCount > 0 ? page.SampleCount : splitBytes[i].Length * 8;
                importResults[i] = BinaryWaveformImporter.ImportBytes(channelNames[i], splitBytes[i], sampleInterval, exactSampleCount);
            }

            if (importResults.Any(result => result == null || result.SampleCount <= 0))
            {
                MessageBox.Show(this, "无法将导入的协议数据转换为波形。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isUartImport = _currentProtocolImportSession.ProtocolType == SerialProtocolType.Uart;
            int samplesPerBit = isUartImport
                ? _currentProtocolImportSession.UartSamplesPerBit
                : GetSamplesPerBit(page.SampleRate, page.DataRate);
            if (samplesPerBit <= 0)
            {
                MessageBox.Show(this, "每位重复采样点数无效。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                        ApplyImportedUartDecodeSettings(_chartSignals[i], _currentProtocolImportSession, page);
                    }
                    else
                    {
                        ApplyImportedProtocolDecodeSettings(_chartSignals[i], samplesPerBit, _currentProtocolImportSession.BitOrder);
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

            ProtocolPageManifest manifest;
            if (ProtocolPageManifestStorage.TryLoad(folderPath, out manifest))
            {
                List<ProtocolImportPageItem> manifestPages = BuildProtocolImportPagesFromManifest(folderPath, protocolType, manifest);
                if (manifestPages.Count > 0)
                {
                    return manifestPages;
                }
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
                            "第 {0} 页（文件 {1}，分段 {2}）",
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
                            "第 {0} 页（文件 {1}，分段 {2}）",
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
                    "第 {0} 页（文件 {1}，分段 {2}）",
                    i + 1,
                    orderedPages[i].FilePageNumber,
                    orderedPages[i].PartitionNumber);
            }

            return orderedPages;
        }

        private static List<ProtocolImportPageItem> BuildProtocolImportPagesFromManifest(
            string folderPath,
            SerialProtocolType protocolType,
            ProtocolPageManifest manifest)
        {
            List<ProtocolImportPageItem> pages = new List<ProtocolImportPageItem>();
            if (string.IsNullOrWhiteSpace(folderPath) || manifest == null || manifest.Pages == null || manifest.Pages.Count == 0)
            {
                return pages;
            }

            SerialProtocolType manifestProtocolType;
            if (TryParseManifestProtocolType(manifest.ProtocolType, out manifestProtocolType) == false || manifestProtocolType != protocolType)
            {
                return pages;
            }

            for (int i = 0; i < manifest.Pages.Count; i++)
            {
                ProtocolPageManifestPage manifestPage = manifest.Pages[i];
                if (manifestPage == null || string.IsNullOrWhiteSpace(manifestPage.FileName))
                {
                    continue;
                }

                string filePath = Path.Combine(folderPath, manifestPage.FileName);
                if (File.Exists(filePath) == false)
                {
                    continue;
                }

                long fileLength = new FileInfo(filePath).Length;
                int channelCount = manifestPage.ChannelCount > 0 ? manifestPage.ChannelCount : manifest.LineCount;
                int bytesPerChannel = manifestPage.BytesPerChannel > 0
                    ? manifestPage.BytesPerChannel
                    : (channelCount > 0 ? (int)(fileLength / channelCount) : 0);
                int byteCount = bytesPerChannel > 0 && channelCount > 0
                    ? bytesPerChannel * channelCount
                    : (fileLength > int.MaxValue ? int.MaxValue : (int)fileLength);
                int pageNumber = manifestPage.PageNumber > 0 ? manifestPage.PageNumber : pages.Count + 1;

                pages.Add(new ProtocolImportPageItem
                {
                    PageNumber = pageNumber,
                    DisplayName = string.Format(CultureInfo.InvariantCulture, "第 {0} 页", pageNumber),
                    FilePath = filePath,
                    FilePageNumber = pageNumber,
                    PartitionNumber = 1,
                    OffsetBytes = 0,
                    ByteCount = byteCount,
                    SampleRate = manifest.SampleRate,
                    DataRate = manifest.DataRate,
                    IsActivePartition = manifestPage.IsActiveData ?? true,
                    ChannelCount = channelCount,
                    BytesPerChannel = bytesPerChannel,
                    SampleCount = manifestPage.SampleCount,
                    StartSampleIndex = manifestPage.StartSampleIndex,
                    LeadingHighSamples = manifestPage.LeadingHighSamples
                });
            }

            return pages.OrderBy(page => page.PageNumber).ToList();
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

        private static byte[][] SplitProtocolBytes(byte[] protocolBytes, int channelCount, int bytesPerChannel)
        {
            if (protocolBytes == null
                || protocolBytes.Length == 0
                || channelCount <= 0
                || bytesPerChannel <= 0
                || protocolBytes.Length < channelCount * bytesPerChannel)
            {
                return null;
            }

            byte[][] channels = new byte[channelCount][];
            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                channels[channelIndex] = new byte[bytesPerChannel];
                Buffer.BlockCopy(protocolBytes, channelIndex * bytesPerChannel, channels[channelIndex], 0, bytesPerChannel);
            }

            return channels;
        }

        private static void ApplyImportedProtocolDecodeSettings(ChartSignal signal, int samplesPerBit, ProtocolBitOrder bitOrder)
        {
            if (signal == null || signal.DecodeSettings == null)
            {
                return;
            }

            signal.DecodeSettings.Mode = SignalDecodeMode.FixedWidth8Bit;
            signal.DecodeSettings.DataBits = 8;
            signal.DecodeSettings.LeadingIdleSamples = 0;
            signal.DecodeSettings.SamplesPerBit = samplesPerBit;
            signal.DecodeSettings.FixedWidthBitOrder = bitOrder;
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
            signal.DecodeSettings.LeadingIdleSamples = 0;
            signal.DecodeSettings.SamplesPerBit = selection.UartSamplesPerBit;
        }

        private static void ApplyImportedUartDecodeSettings(ChartSignal signal, ProtocolImportSession session, ProtocolImportPageItem page)
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
            signal.DecodeSettings.LeadingIdleSamples = page == null ? 0 : Math.Max(0, page.LeadingHighSamples);
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
                case "无":
                case "无校验":
                case "None":
                    parityMode = UartParityMode.None;
                    return true;
                case "奇":
                case "奇校验":
                case "Odd":
                    parityMode = UartParityMode.Odd;
                    return true;
                case "偶":
                case "偶校验":
                case "Even":
                    parityMode = UartParityMode.Even;
                    return true;
                case "标记":
                case "标记校验":
                case "Mark":
                    parityMode = UartParityMode.Mark;
                    return true;
                case "空格":
                case "空格校验":
                case "Space":
                    parityMode = UartParityMode.Space;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseProtocolBitOrder(string text, out ProtocolBitOrder bitOrder)
        {
            bitOrder = ProtocolBitOrder.BigEndian;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            switch (text.Trim())
            {
                case "BigEndian":
                case "MSB":
                case "MSBFirst":
                case "大端":
                    bitOrder = ProtocolBitOrder.BigEndian;
                    return true;
                case "LittleEndian":
                case "LSB":
                case "LSBFirst":
                case "小端":
                    bitOrder = ProtocolBitOrder.LittleEndian;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseManifestProtocolType(string text, out SerialProtocolType protocolType)
        {
            protocolType = SerialProtocolType.Uart;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return Enum.TryParse(text.Trim(), true, out protocolType);
        }

        private static bool TryGetProtocolTypeFromLineCount(int lineCount, out SerialProtocolType protocolType)
        {
            switch (lineCount)
            {
                case 2:
                    protocolType = SerialProtocolType.TwoWireSerial;
                    return true;
                case 3:
                    protocolType = SerialProtocolType.ThreeWireSerial;
                    return true;
                case 4:
                    protocolType = SerialProtocolType.FourWireSerial;
                    return true;
                default:
                    protocolType = SerialProtocolType.Uart;
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
                    return "2线串口";
                case SerialProtocolType.ThreeWireSerial:
                    return "3线串口";
                case SerialProtocolType.FourWireSerial:
                    return "4线串口";
                default:
                    return "单通道";
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

