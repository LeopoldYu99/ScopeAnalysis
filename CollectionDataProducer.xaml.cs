using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace ScopeAnalysis
{
    public partial class CollectionDataProducer : UserControl
    {
        private const uint FrameHeader = 0x499602D2;
        private const uint FrameFooter = 0xB669FD2E;
        private const int MinimumFrameLength = 16;
        private const int ReceiveBufferTrimLength = 1024 * 1024;

        private bool _isCollecting;
        private CancellationTokenSource _collectionCancellation;
        private Task<CollectionResult> _collectionTask;
        private UdpClient _udpClient;
        private string _currentExportDirectory;

        private sealed class CollectionSession
        {
            public SerialProtocolType ProtocolType { get; set; }
            public uint SampleRate { get; set; }
            public uint DataRate { get; set; }
            public int BaudRate { get; set; }
            public UartParityMode ParityMode { get; set; }
            public int DataBits { get; set; }
            public double StopBits { get; set; }
            public int SamplesPerBit { get; set; }
            public ProtocolBitOrder BitOrder { get; set; }
            public int LineCount { get; set; }
            public int PageSizeBytes { get; set; }
            public int LocalPort { get; set; }
            public DateTime ExportTimestamp { get; set; }
            public string ExportDirectory { get; set; }
        }

        private sealed class CollectionResult
        {
            public string ExportDirectory { get; set; }
            public int PageCount { get; set; }
            public long ReceivedDatagramCount { get; set; }
            public long ValidFrameCount { get; set; }
            public long InvalidFrameCount { get; set; }
            public long PayloadByteCount { get; set; }
            public long ExportByteCount { get; set; }
            public TimeSpan Elapsed { get; set; }
        }

        public CollectionDataProducer()
        {
            InitializeComponent();

            ProtocolTypeComboBox.SelectionChanged += ProtocolTypeComboBox_SelectionChanged;
            SampleRateComboBox.SelectionChanged += SettingsAffectingBitIntervalChanged;

            SelectComboBoxItemByText(SampleRateComboBox, "50M");
            SelectComboBoxItemByText(BaudRateComboBox, "115200");
            SelectComboBoxItemByText(ParityComboBox, "无校验");
            SelectComboBoxItemByText(DataBitsComboBox, "8");
            SelectComboBoxItemByText(StopBitsComboBox, "1");
            SelectComboBoxItemByText(ProtocolBitOrderComboBox, ProtocolBitOrder.BigEndian.ToString());

            ApplyProtocolVisibility(GetSelectedProtocolType());
            UpdateBitIntervalDisplay();
        }

        private async void CollectionToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCollecting)
            {
                await StopCollectionAsync(true);
                return;
            }

            StartCollection();
        }

        private void StartCollection()
        {
            try
            {
                string exportParentDirectory = SelectCollectionExportDirectory();
                if (string.IsNullOrWhiteSpace(exportParentDirectory))
                {
                    return;
                }

                CollectionSession session = CreateCollectionSession(exportParentDirectory);
                Directory.CreateDirectory(session.ExportDirectory);

                _collectionCancellation = new CancellationTokenSource();
                _isCollecting = true;
                _currentExportDirectory = session.ExportDirectory;
                SetCollectionUiState(true, false);

                _collectionTask = Task.Run(
                    () => RunCollection(session, _collectionCancellation.Token));

                _collectionTask.ContinueWith(
                    task => Dispatcher.BeginInvoke(new Action(() => HandleCollectionTaskCompleted(task))),
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _isCollecting = false;
                SetCollectionUiState(false, false);
                MessageBox.Show(
                    "采集启动失败: " + ex.Message,
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task StopCollectionAsync(bool showResult)
        {
            if (_isCollecting == false)
            {
                return;
            }

            CancellationTokenSource cancellation = _collectionCancellation;
            if (cancellation != null && cancellation.IsCancellationRequested == false)
            {
                cancellation.Cancel();
            }

            CloseUdpClient();
            SetCollectionUiState(true, true);

            Task<CollectionResult> task = _collectionTask;
            if (task != null)
            {
                try
                {
                    CollectionResult result = await task;
                    if (showResult)
                    {
                        ShowCollectionStoppedMessage(result);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "采集停止失败: " + GetExceptionMessage(ex),
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            _isCollecting = false;
            _collectionCancellation = null;
            _collectionTask = null;
            SetCollectionUiState(false, false);
        }

        private void HandleCollectionTaskCompleted(Task<CollectionResult> task)
        {
            if (task == null || task != _collectionTask || _isCollecting == false)
            {
                return;
            }

            _isCollecting = false;
            _collectionCancellation = null;
            _collectionTask = null;
            SetCollectionUiState(false, false);

            if (task.IsFaulted)
            {
                MessageBox.Show(
                    "采集已停止: " + GetExceptionMessage(task.Exception),
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SetCollectionUiState(bool isCollecting, bool isStopping)
        {
            if (CollectionToggleButton != null)
            {
                CollectionToggleButton.Content = isCollecting
                    ? (isStopping ? "正在停止..." : "停止")
                    : "采集";
                CollectionToggleButton.IsEnabled = isStopping == false;
            }

            if (CollectionStatusTextBlock != null)
            {
                CollectionStatusTextBlock.Text = isCollecting
                    ? "正在采集: " + (_currentExportDirectory ?? string.Empty)
                    : "2/3/4 线按采样率 / 数据速率展开采样。";
            }
        }

        private void ProtocolTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyProtocolVisibility(GetSelectedProtocolType());
        }

        private void SettingsAffectingBitIntervalChanged(object sender, EventArgs e)
        {
            UpdateBitIntervalDisplay();
        }

        private void UpdateBitIntervalDisplay()
        {
            if (BitIntervalNsTextBox == null)
            {
                return;
            }

            double bitIntervalNanoseconds;
            if (TryGetBitIntervalNanoseconds(out bitIntervalNanoseconds))
            {
                BitIntervalNsTextBox.Text = bitIntervalNanoseconds.ToString("0.###", CultureInfo.InvariantCulture);
                return;
            }

            BitIntervalNsTextBox.Text = string.Empty;
        }

        private bool TryGetBitIntervalNanoseconds(out double bitIntervalNanoseconds)
        {
            uint sampleRate;
            if (TryGetSelectedSampleRate(out sampleRate) == false || sampleRate == 0)
            {
                bitIntervalNanoseconds = 0;
                return false;
            }

            bitIntervalNanoseconds = 1000000000d / sampleRate;
            return true;
        }

        private bool TryGetSelectedSampleRate(out uint sampleRate)
        {
            sampleRate = 0;
            if (SampleRateComboBox == null)
            {
                return false;
            }

            ComboBoxItem selectedItem = SampleRateComboBox.SelectedItem as ComboBoxItem;
            string text = selectedItem == null ? SampleRateComboBox.Text : selectedItem.Content as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            switch (text.Trim().ToUpperInvariant())
            {
                case "10M":
                    sampleRate = 10000000;
                    return true;
                case "20M":
                    sampleRate = 20000000;
                    return true;
                case "25M":
                    sampleRate = 25000000;
                    return true;
                case "50M":
                    sampleRate = 50000000;
                    return true;
                default:
                    return false;
            }
        }

        private CollectionSession CreateCollectionSession(string exportParentDirectory)
        {
            uint sampleRate;
            if (TryGetSelectedSampleRate(out sampleRate) == false || sampleRate == 0)
            {
                throw new InvalidOperationException("采样率必须是有效值。");
            }

            SerialProtocolType protocolType = GetSelectedProtocolType();
            DateTime exportTimestamp = DateTime.Now;
            CollectionSession session = new CollectionSession
            {
                ProtocolType = protocolType,
                SampleRate = sampleRate,
                PageSizeBytes = GetPageSizeBytes(),
                LocalPort = GetLocalPort(),
                ExportTimestamp = exportTimestamp
            };

            if (protocolType == SerialProtocolType.Uart)
            {
                session.BaudRate = GetSelectedBaudRate();

                session.SamplesPerBit = GetRoundedSamplesPerBit(sampleRate, (uint)session.BaudRate, "波特率");
                session.ParityMode = GetSelectedParityMode();
                session.DataBits = GetSelectedDataBits();
                session.StopBits = GetSelectedStopBits();
                session.LineCount = 1;
                session.ExportDirectory = Path.Combine(
                    exportParentDirectory,
                    ProtocolBinNaming.BuildUartExportFolderName(
                        sampleRate,
                        session.BaudRate,
                        session.ParityMode.ToString(),
                        session.DataBits,
                        session.StopBits,
                        exportTimestamp));
                return session;
            }

            session.DataRate = GetSelectedDataRate(protocolType);

            session.SamplesPerBit = GetRoundedSamplesPerBit(sampleRate, session.DataRate, "数据速率");
            session.BitOrder = GetSelectedBitOrder();
            session.LineCount = GetProtocolLineCount(protocolType);
            session.ExportDirectory = Path.Combine(
                exportParentDirectory,
                ProtocolBinNaming.BuildExportFolderName(session.LineCount, sampleRate, session.DataRate, session.BitOrder, exportTimestamp));
            return session;
        }

        private static int GetRoundedSamplesPerBit(uint sampleRate, uint dataRate, string rateName)
        {
            if (sampleRate == 0 || dataRate == 0)
            {
                throw new InvalidOperationException(rateName + "必须是有效值。");
            }

            double samplesPerBit = sampleRate / (double)dataRate;
            int roundedSamplesPerBit = (int)Math.Round(samplesPerBit, MidpointRounding.AwayFromZero);
            if (roundedSamplesPerBit <= 0)
            {
                throw new InvalidOperationException("采样率相对" + rateName + "过低。");
            }

            return roundedSamplesPerBit;
        }

        private CollectionResult RunCollection(CollectionSession session, CancellationToken cancellationToken)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }

            ProtocolPageManifest manifest = CreateCollectionManifest(session);
            ProtocolPageManifestStorage.Save(session.ExportDirectory, manifest);

            CollectionResult result = new CollectionResult
            {
                ExportDirectory = session.ExportDirectory
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<byte> receiveBuffer = new List<byte>();
            byte[] pageBuffer = new byte[session.PageSizeBytes];
            int pageBufferLength = 0;
            long totalSamples = 0;

            using (UdpClient udpClient = new UdpClient(session.LocalPort))
            {
                _udpClient = udpClient;
                udpClient.Client.ReceiveTimeout = 500;
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                while (cancellationToken.IsCancellationRequested == false)
                {
                    byte[] datagram;
                    try
                    {
                        datagram = udpClient.Receive(ref remoteEndPoint);
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode == SocketError.TimedOut || cancellationToken.IsCancellationRequested)
                        {
                            continue;
                        }

                        throw;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (datagram == null || datagram.Length == 0)
                    {
                        continue;
                    }

                    result.ReceivedDatagramCount++;
                    AppendBytes(receiveBuffer, datagram);
                    List<byte[]> payloads = ExtractPayloads(receiveBuffer, result);
                    for (int i = 0; i < payloads.Count; i++)
                    {
                        byte[] payload = payloads[i];
                        if (payload == null || payload.Length == 0)
                        {
                            continue;
                        }

                        result.PayloadByteCount += payload.Length;
                        WritePayloadToPages(
                            payload,
                            session,
                            manifest,
                            pageBuffer,
                            ref pageBufferLength,
                            ref totalSamples,
                            result);
                    }
                }
            }

            if (pageBufferLength > 0)
            {
                FlushPage(
                    session,
                    manifest,
                    pageBuffer,
                    pageBufferLength,
                    ref pageBufferLength,
                    ref totalSamples,
                    result);
            }

            ProtocolPageManifestStorage.Save(session.ExportDirectory, manifest);
            stopwatch.Stop();
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        private static ProtocolPageManifest CreateCollectionManifest(CollectionSession session)
        {
            return new ProtocolPageManifest
            {
                Version = 1,
                ProtocolType = session.ProtocolType.ToString(),
                LineCount = session.LineCount,
                SampleRate = session.SampleRate,
                DataRate = session.ProtocolType == SerialProtocolType.Uart ? 0 : session.DataRate,
                BaudRate = session.ProtocolType == SerialProtocolType.Uart ? Math.Max(0, session.BaudRate) : 0,
                ParityText = session.ProtocolType == SerialProtocolType.Uart ? session.ParityMode.ToString() : string.Empty,
                DataBits = session.ProtocolType == SerialProtocolType.Uart ? session.DataBits : 8,
                StopBits = session.ProtocolType == SerialProtocolType.Uart ? session.StopBits : 0,
                SamplesPerBit = session.SamplesPerBit,
                BitOrder = session.ProtocolType == SerialProtocolType.Uart ? string.Empty : session.BitOrder.ToString(),
                PageDurationSeconds = 0,
                TimestampText = BuildManifestTimestampText(session.ExportTimestamp),
                Pages = new List<ProtocolPageManifestPage>()
            };
        }

        private static void WritePayloadToPages(
            byte[] payload,
            CollectionSession session,
            ProtocolPageManifest manifest,
            byte[] pageBuffer,
            ref int pageBufferLength,
            ref long totalSamples,
            CollectionResult result)
        {
            int payloadOffset = 0;
            while (payloadOffset < payload.Length)
            {
                int copyLength = Math.Min(pageBuffer.Length - pageBufferLength, payload.Length - payloadOffset);
                Buffer.BlockCopy(payload, payloadOffset, pageBuffer, pageBufferLength, copyLength);
                payloadOffset += copyLength;
                pageBufferLength += copyLength;

                if (pageBufferLength >= pageBuffer.Length)
                {
                    FlushPage(
                        session,
                        manifest,
                        pageBuffer,
                        pageBufferLength,
                        ref pageBufferLength,
                        ref totalSamples,
                        result);
                }
            }
        }

        private static void FlushPage(
            CollectionSession session,
            ProtocolPageManifest manifest,
            byte[] pageBuffer,
            int pageBufferLength,
            ref int pageBufferLengthReference,
            ref long totalSamples,
            CollectionResult result)
        {
            if (pageBufferLength <= 0)
            {
                return;
            }

            int writeLength = GetAlignedPageLength(pageBufferLength, session.LineCount);
            if (writeLength <= 0)
            {
                pageBufferLengthReference = 0;
                return;
            }

            int pageNumber = manifest.Pages.Count + 1;
            string fileName = ProtocolBinNaming.BuildPageFileName(pageNumber);
            string filePath = Path.Combine(session.ExportDirectory, fileName);

            using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(pageBuffer, 0, writeLength);
            }

            int channelCount = Math.Max(1, session.LineCount);
            int bytesPerChannel = session.ProtocolType == SerialProtocolType.Uart
                ? writeLength
                : writeLength / channelCount;
            int sampleCount = checked(bytesPerChannel * 8);
            ProtocolPageManifestPage page = new ProtocolPageManifestPage
            {
                PageNumber = pageNumber,
                FileName = fileName,
                StartSampleIndex = totalSamples,
                SampleCount = sampleCount,
                ChannelCount = channelCount,
                BytesPerChannel = bytesPerChannel,
                LeadingHighSamples = 0,
                IsActiveData = true,
                StartTimeSeconds = session.SampleRate == 0 ? 0 : totalSamples / (double)session.SampleRate,
                DurationSeconds = session.SampleRate == 0 ? 0 : sampleCount / (double)session.SampleRate,
                StartsOnBoundary = true,
                EndsOnBoundary = true
            };

            manifest.Pages.Add(page);
            ProtocolPageManifestStorage.Save(session.ExportDirectory, manifest);
            totalSamples += sampleCount;
            result.PageCount++;
            result.ExportByteCount += writeLength;

            int remainingLength = pageBufferLength - writeLength;
            if (remainingLength > 0)
            {
                Buffer.BlockCopy(pageBuffer, writeLength, pageBuffer, 0, remainingLength);
            }

            pageBufferLengthReference = remainingLength;
        }

        private static List<byte[]> ExtractPayloads(List<byte> receiveBuffer, CollectionResult result)
        {
            List<byte[]> payloads = new List<byte[]>();
            while (receiveBuffer.Count >= MinimumFrameLength)
            {
                int headerIndex = FindFrameHeader(receiveBuffer);
                if (headerIndex < 0)
                {
                    TrimReceiveBufferWithoutHeader(receiveBuffer);
                    break;
                }

                if (headerIndex > 0)
                {
                    receiveBuffer.RemoveRange(0, headerIndex);
                }

                if (receiveBuffer.Count < MinimumFrameLength)
                {
                    break;
                }

                int dataLength = ReadUInt16BigEndian(receiveBuffer, 4);
                if (dataLength < 4)
                {
                    receiveBuffer.RemoveAt(0);
                    result.InvalidFrameCount++;
                    continue;
                }

                int frameLength = 4 + 2 + dataLength + 2 + 4;
                if (receiveBuffer.Count < frameLength)
                {
                    break;
                }

                uint footer = ReadUInt32BigEndian(receiveBuffer, frameLength - 4);
                if (footer != FrameFooter)
                {
                    receiveBuffer.RemoveAt(0);
                    result.InvalidFrameCount++;
                    continue;
                }

                ushort expectedChecksum = ReadUInt16BigEndian(receiveBuffer, 4 + 2 + dataLength);
                ushort actualChecksum = CalculateChecksum(receiveBuffer, 4, 2 + dataLength);
                if (expectedChecksum != actualChecksum)
                {
                    receiveBuffer.RemoveAt(0);
                    result.InvalidFrameCount++;
                    continue;
                }

                int payloadLength = dataLength - 4;
                if (payloadLength > 0)
                {
                    byte[] payload = new byte[payloadLength];
                    for (int i = 0; i < payloadLength; i++)
                    {
                        payload[i] = receiveBuffer[10 + i];
                    }

                    payloads.Add(payload);
                }

                result.ValidFrameCount++;
                receiveBuffer.RemoveRange(0, frameLength);
            }

            return payloads;
        }

        private static int FindFrameHeader(List<byte> receiveBuffer)
        {
            for (int i = 0; i <= receiveBuffer.Count - 4; i++)
            {
                if (ReadUInt32BigEndian(receiveBuffer, i) == FrameHeader)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void TrimReceiveBufferWithoutHeader(List<byte> receiveBuffer)
        {
            if (receiveBuffer.Count <= 3)
            {
                return;
            }

            receiveBuffer.RemoveRange(0, receiveBuffer.Count - 3);
        }

        private static void AppendBytes(List<byte> receiveBuffer, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                receiveBuffer.Add(bytes[i]);
            }

            if (receiveBuffer.Count > ReceiveBufferTrimLength)
            {
                receiveBuffer.RemoveRange(0, receiveBuffer.Count - 3);
            }
        }

        private static ushort CalculateChecksum(List<byte> bytes, int offset, int length)
        {
            int checksum = 0;
            for (int i = 0; i < length; i++)
            {
                checksum = (checksum + bytes[offset + i]) & 0xFFFF;
            }

            return (ushort)checksum;
        }

        private static ushort ReadUInt16BigEndian(List<byte> bytes, int offset)
        {
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }

        private static uint ReadUInt32BigEndian(List<byte> bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static int GetAlignedPageLength(int pageBufferLength, int lineCount)
        {
            if (pageBufferLength <= 0)
            {
                return 0;
            }

            int channelCount = Math.Max(1, lineCount);
            return pageBufferLength - (pageBufferLength % channelCount);
        }

        private int GetPageSizeBytes()
        {
            string text = PageSizeMbTextBox == null ? null : PageSizeMbTextBox.Text;
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false
                && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                throw new InvalidOperationException("页大小必须是有效的正数。");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException("页大小必须大于 0。");
            }

            double bytes = value * 1024d * 1024d;
            if (bytes > int.MaxValue)
            {
                throw new InvalidOperationException("页大小过大。");
            }

            return Math.Max(1, (int)Math.Round(bytes, MidpointRounding.AwayFromZero));
        }

        private int GetLocalPort()
        {
            string text = LocalPortTextBox == null ? null : LocalPortTextBox.Text;
            int port;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) == false || port <= 0 || port > 65535)
            {
                throw new InvalidOperationException("本地端口必须是 1 到 65535 之间的整数。");
            }

            return port;
        }

        private uint GetSelectedDataRate(SerialProtocolType protocolType)
        {
            string text;
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    text = TwoWireSamplesPerBitTextBox == null ? null : TwoWireSamplesPerBitTextBox.Text;
                    break;
                case SerialProtocolType.ThreeWireSerial:
                    text = ThreeWireSamplesPerBitTextBox == null ? null : ThreeWireSamplesPerBitTextBox.Text;
                    break;
                case SerialProtocolType.FourWireSerial:
                    text = FourWireSamplesPerBitTextBox == null ? null : FourWireSamplesPerBitTextBox.Text;
                    break;
                default:
                    throw new InvalidOperationException("当前协议不需要数据速率。");
            }

            uint dataRate;
            if (TryParseFrequency(text, out dataRate) == false || dataRate == 0)
            {
                throw new InvalidOperationException("数据速率必须是有效频率，例如 5M 或 5000000。");
            }

            return dataRate;
        }

        private int GetSelectedBaudRate()
        {
            uint baudRate;
            if (TryParseFrequency(GetComboBoxText(BaudRateComboBox), out baudRate) == false || baudRate == 0 || baudRate > int.MaxValue)
            {
                throw new InvalidOperationException("波特率必须是有效的正数。");
            }

            return (int)baudRate;
        }

        private int GetSelectedDataBits()
        {
            int dataBits;
            if (int.TryParse(GetComboBoxText(DataBitsComboBox), NumberStyles.Integer, CultureInfo.InvariantCulture, out dataBits) == false || dataBits <= 0)
            {
                throw new InvalidOperationException("数据位必须是正整数。");
            }

            return dataBits;
        }

        private double GetSelectedStopBits()
        {
            double stopBits;
            string text = GetComboBoxText(StopBitsComboBox);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out stopBits) == false
                && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out stopBits) == false)
            {
                throw new InvalidOperationException("停止位必须是正数。");
            }

            if (stopBits <= 0)
            {
                throw new InvalidOperationException("停止位必须是正数。");
            }

            return stopBits;
        }

        private UartParityMode GetSelectedParityMode()
        {
            string text = GetComboBoxText(ParityComboBox);
            switch (text)
            {
                case "奇":
                case "奇校验":
                case "Odd":
                    return UartParityMode.Odd;
                case "偶":
                case "偶校验":
                case "Even":
                    return UartParityMode.Even;
                case "标记":
                case "标记校验":
                case "Mark":
                    return UartParityMode.Mark;
                case "空格":
                case "空格校验":
                case "Space":
                    return UartParityMode.Space;
                default:
                    return UartParityMode.None;
            }
        }

        private ProtocolBitOrder GetSelectedBitOrder()
        {
            string text = GetComboBoxText(ProtocolBitOrderComboBox);
            switch ((text ?? string.Empty).Trim())
            {
                case "LittleEndian":
                case "LSB":
                case "LSBFirst":
                case "小端":
                    return ProtocolBitOrder.LittleEndian;
                default:
                    return ProtocolBitOrder.BigEndian;
            }
        }

        private static string GetComboBoxText(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return string.Empty;
            }

            ComboBoxItem selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                return Convert.ToString(selectedItem.Content, CultureInfo.InvariantCulture);
            }

            return comboBox.Text ?? string.Empty;
        }

        private static bool TryParseFrequency(string text, out uint frequency)
        {
            frequency = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalizedText = text.Trim();
            double multiplier = 1;
            if (normalizedText.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000000;
                normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
            }
            else if (normalizedText.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000;
                normalizedText = normalizedText.Substring(0, normalizedText.Length - 1);
            }

            double value;
            if (double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false
                && double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                return false;
            }

            if (value <= 0)
            {
                return false;
            }

            double calculatedFrequency = value * multiplier;
            if (calculatedFrequency <= 0 || calculatedFrequency > uint.MaxValue)
            {
                return false;
            }

            frequency = (uint)Math.Round(calculatedFrequency, MidpointRounding.AwayFromZero);
            return frequency > 0;
        }

        private static int GetProtocolLineCount(SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return 2;
                case SerialProtocolType.ThreeWireSerial:
                    return 3;
                case SerialProtocolType.FourWireSerial:
                    return 4;
                default:
                    return 1;
            }
        }

        private static string BuildManifestTimestampText(DateTime exportTimestamp)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}_{2}_{3}_{4}_{5}_{6}",
                exportTimestamp.Year,
                exportTimestamp.Month,
                exportTimestamp.Day,
                exportTimestamp.Hour,
                exportTimestamp.Minute,
                exportTimestamp.Second,
                exportTimestamp.Millisecond);
        }

        private static string SelectCollectionExportDirectory()
        {
            using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "选择保存采集 BIN 文件的文件夹。";
                folderDialog.ShowNewFolderButton = true;
                return folderDialog.ShowDialog() == Forms.DialogResult.OK
                    ? folderDialog.SelectedPath
                    : null;
            }
        }

        private void CloseUdpClient()
        {
            try
            {
                UdpClient udpClient = _udpClient;
                if (udpClient != null)
                {
                    udpClient.Close();
                }
            }
            catch
            {
            }
            finally
            {
                _udpClient = null;
            }
        }

        private void ShowCollectionStoppedMessage(CollectionResult result)
        {
            if (result == null)
            {
                return;
            }

            MessageBox.Show(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "采集已停止。{0}{0}目录: {1}{0}页数: {2:N0}{0}UDP 包: {3:N0}{0}有效帧: {4:N0}{0}无效帧: {5:N0}{0}数据区字节: {6:N0}{0}导出字节: {7:N0}",
                    Environment.NewLine,
                    result.ExportDirectory,
                    result.PageCount,
                    result.ReceivedDatagramCount,
                    result.ValidFrameCount,
                    result.InvalidFrameCount,
                    result.PayloadByteCount,
                    result.ExportByteCount),
                "已停止",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static string GetExceptionMessage(Exception exception)
        {
            AggregateException aggregateException = exception as AggregateException;
            if (aggregateException != null)
            {
                exception = aggregateException.GetBaseException();
            }

            return exception == null ? "未知错误" : exception.Message;
        }

        private SerialProtocolType GetSelectedProtocolType()
        {
            if (ProtocolTypeComboBox == null)
            {
                return SerialProtocolType.Uart;
            }

            switch (ProtocolTypeComboBox.SelectedIndex)
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

        private void ApplyProtocolVisibility(SerialProtocolType protocolType)
        {
            if (UartConfigPanel == null)
            {
                return;
            }

            UartConfigPanel.Visibility = protocolType == SerialProtocolType.Uart ? Visibility.Visible : Visibility.Collapsed;
            TwoWireConfigPanel.Visibility = protocolType == SerialProtocolType.TwoWireSerial ? Visibility.Visible : Visibility.Collapsed;
            ThreeWireConfigPanel.Visibility = protocolType == SerialProtocolType.ThreeWireSerial ? Visibility.Visible : Visibility.Collapsed;
            FourWireConfigPanel.Visibility = protocolType == SerialProtocolType.FourWireSerial ? Visibility.Visible : Visibility.Collapsed;

            if (ProtocolBitOrderPanel != null)
            {
                ProtocolBitOrderPanel.Visibility = protocolType == SerialProtocolType.Uart ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private static void SelectComboBoxItemByText(ComboBox comboBox, string expectedText)
        {
            if (comboBox == null || string.IsNullOrEmpty(expectedText))
            {
                return;
            }

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                ComboBoxItem item = comboBox.Items[i] as ComboBoxItem;
                if (item == null)
                {
                    continue;
                }

                string itemText = item.Content as string;
                if (string.Equals(itemText, expectedText, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }
    }
}
