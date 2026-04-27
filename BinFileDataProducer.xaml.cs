using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ScopeAnalysis;
using Forms = System.Windows.Forms;

namespace ScopeAnalysis
{
    public partial class BinFileDataProducer : UserControl
    {
        private const int HexBytesPerLine = 16;
        private const int ProtocolExportChunkSeconds = 5;
        private const double ProtocolExportPartitionDurationSeconds = 0.1;
        private const int ProtocolExportPartitionsPerChunk = (int)(ProtocolExportChunkSeconds / ProtocolExportPartitionDurationSeconds);
        private const double MinimumEmptySegmentDurationMilliseconds = 20;
        private const double MinimumEmptySegmentIntervalMilliseconds = 40;
        private const byte ProtocolClockBitPattern = 0xAA;

        private CancellationTokenSource _streamGenerationCancellation;
        private Task<StreamingGenerationResult> _streamGenerationTask;

        private sealed class StreamingGenerationSession
        {
            public SerialProtocolType ProtocolType { get; set; }
            public uint SampleRate { get; set; }
            public uint DataRate { get; set; }
            public int BaudRate { get; set; }
            public UartParityMode ParityMode { get; set; }
            public int DataBits { get; set; }
            public double StopBits { get; set; }
            public int SamplesPerBit { get; set; }
            public int LineCount { get; set; }
            public double PageDurationSeconds { get; set; }
            public byte EmptyDataValue { get; set; }
            public int EmptyDataRatio { get; set; }
            public byte[] SeedBytes { get; set; }
            public int RandomSeed { get; set; }
            public DateTime ExportTimestamp { get; set; }
            public string ExportDirectory { get; set; }
        }

        private sealed class StreamingGenerationResult
        {
            public string ExportDirectory { get; set; }
            public int PageCount { get; set; }
            public long PayloadByteCount { get; set; }
            public long ExportByteCount { get; set; }
            public TimeSpan Elapsed { get; set; }
        }

        private sealed class StreamingPageWriteResult
        {
            public ProtocolPageManifestPage Page { get; set; }
            public long PayloadByteCount { get; set; }
            public long ExportByteCount { get; set; }
        }

        public BinFileDataProducer()
        {
            InitializeComponent();

            PayloadAsciiTextBox.TextChanged += PayloadAsciiTextBox_TextChanged;
            ProtocolTypeComboBox.SelectionChanged += ProtocolTypeComboBox_SelectionChanged;
            SampleRateComboBox.SelectionChanged += SettingsAffectingBitIntervalChanged;

            SelectComboBoxItemByText(SampleRateComboBox, "50M");
            SelectComboBoxItemByText(BaudRateComboBox, "115200");
            SelectComboBoxItemByText(ParityComboBox, "无校验");
            SelectComboBoxItemByText(DataBitsComboBox, "8");
            SelectComboBoxItemByText(StopBitsComboBox, "1");

            ApplyProtocolVisibility(GetSelectedProtocolType());
            UpdatePayloadPreview();
            UpdateBitIntervalDisplay();
        }

        private void PayloadAsciiTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePayloadPreview();
        }

        private void ProtocolTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyProtocolVisibility(GetSelectedProtocolType());
        }

        private void SettingsAffectingBitIntervalChanged(object sender, EventArgs e)
        {
            UpdateBitIntervalDisplay();
        }

        private void GenerateBinButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SerialProtocolType protocolType = GetSelectedProtocolType();
                if (protocolType == SerialProtocolType.Uart)
                {
                    GenerateUartBin();
                    return;
                }
                uint sampleRate;
                if (TryGetSelectedSampleRate(out sampleRate) == false || sampleRate == 0)
                {
                    throw new InvalidOperationException("采样率必须是有效值。");
                }

                uint dataRate = GetSelectedDataRate(protocolType);
                if (sampleRate % dataRate != 0)
                {
                    throw new InvalidOperationException("采样率必须是数据速率的整数倍。");
                }

                int samplesPerBit = checked((int)(sampleRate / dataRate));
                double durationSeconds = GetDurationSeconds();
                double pageDurationSeconds = GetPageDurationSeconds();
                byte emptyDataValue = GetEmptyDataValue();
                int emptyDataRatio = GetEmptyDataRatio();
                byte[] payloadBytes = BuildLogicalPayloadBytes(
                    Encoding.ASCII.GetBytes(PayloadAsciiTextBox.Text ?? string.Empty),
                    GetLogicalDataByteCount(dataRate, durationSeconds),
                    emptyDataRatio,
                    emptyDataValue,
                    Environment.TickCount,
                    dataRate);
                byte[] payloadBytes2 = protocolType == SerialProtocolType.FourWireSerial
                    ? BuildSecondaryLogicalPayloadBytes(payloadBytes, emptyDataValue)
                    : new byte[0];

                byte[] protocolBytes = BuildSampledProtocolBytes(
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    GetClockValue(protocolType),
                    emptyDataValue,
                    samplesPerBit);

                if (protocolBytes.Length == 0)
                {
                    throw new InvalidOperationException("未生成任何协议数据。");
                }

                string exportParentDirectory = SelectProtocolExportDirectory();
                if (string.IsNullOrWhiteSpace(exportParentDirectory))
                {
                    return;
                }

                int lineCount = GetProtocolLineCount(protocolType);
                DateTime exportTimestamp = DateTime.Now;
                string exportDirectory = Path.Combine(
                    exportParentDirectory,
                    ProtocolBinNaming.BuildExportFolderName(lineCount, sampleRate, dataRate, exportTimestamp));
                Directory.CreateDirectory(exportDirectory);

                int pageCount = WriteProtocolPages(
                    protocolBytes,
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    lineCount,
                    sampleRate,
                    dataRate,
                    samplesPerBit,
                    emptyDataValue,
                    pageDurationSeconds,
                    exportTimestamp,
                    exportDirectory);

                MessageBox.Show(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "BIN 生成完成。{0}{0}协议: {1}{0}目录: {2}{0}文件数: {3:N0}{0}采样率: {4:N0} Hz{0}数据速率: {5:N0} bps{0}每位重复采样点: {6:N0}{0}逻辑数据字节: {7:N0}{0}导出字节: {8:N0}",
                        Environment.NewLine,
                        GetProtocolDisplayName(protocolType),
                        exportDirectory,
                        pageCount,
                        sampleRate,
                        dataRate,
                        samplesPerBit,
                        payloadBytes.Length,
                        protocolBytes.Length),
                    "成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("BIN 生成失败:\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateUartBin()
        {
            uint sampleRate;
            if (TryGetSelectedSampleRate(out sampleRate) == false || sampleRate == 0)
            {
                throw new InvalidOperationException("采样率无效。");
            }

            int baudRate = GetSelectedBaudRate();
            int samplesPerBit = GetRoundedSamplesPerBit(sampleRate, baudRate);
            int dataBits = GetSelectedDataBits();
            double stopBits = GetSelectedStopBits();
            UartParityMode parityMode = GetSelectedParityMode();
            double durationSeconds = GetDurationSeconds();
            double pageDurationSeconds = GetPageDurationSeconds();
            byte emptyDataValue = GetEmptyDataValue();
            long payloadByteCount = GetUartPayloadByteCount(sampleRate, durationSeconds, samplesPerBit, dataBits, stopBits, parityMode);
            byte[] payloadBytes = BuildLogicalPayloadBytes(
                Encoding.ASCII.GetBytes(PayloadAsciiTextBox.Text ?? string.Empty),
                payloadByteCount,
                GetEmptyDataRatio(),
                emptyDataValue,
                Environment.TickCount,
                (uint)Math.Max(1, baudRate));
            byte[] uartBytes = BuildSampledUartBytes(payloadBytes, emptyDataValue, samplesPerBit, dataBits, stopBits, parityMode);
            if (uartBytes.Length == 0)
            {
                throw new InvalidOperationException("未生成任何串口数据。");
            }

            string exportParentDirectory = SelectProtocolExportDirectory();
            if (string.IsNullOrWhiteSpace(exportParentDirectory))
            {
                return;
            }

            DateTime exportTimestamp = DateTime.Now;
            string exportDirectory = Path.Combine(
                exportParentDirectory,
                ProtocolBinNaming.BuildUartExportFolderName(
                    sampleRate,
                    baudRate,
                    parityMode.ToString(),
                    dataBits,
                    stopBits,
                    exportTimestamp));
            Directory.CreateDirectory(exportDirectory);

            int pageCount = WriteUartPages(
                uartBytes,
                payloadBytes,
                sampleRate,
                baudRate,
                parityMode,
                dataBits,
                stopBits,
                samplesPerBit,
                emptyDataValue,
                pageDurationSeconds,
                exportTimestamp,
                exportDirectory);

            MessageBox.Show(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "串口 BIN 已生成。{0}{0}目录: {1}{0}页数: {2:N0}{0}采样率: {3:N0} Hz{0}波特率: {4:N0} bps{0}每位重复采样点: {5:N0}{0}载荷字节数: {6:N0}{0}导出字节数: {7:N0}",
                    Environment.NewLine,
                    exportDirectory,
                    pageCount,
                    sampleRate,
                    baudRate,
                    samplesPerBit,
                    payloadBytes.Length,
                    uartBytes.Length),
                "成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void StreamGenerateBinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_streamGenerationCancellation != null)
            {
                StopStreamingGeneration();
                return;
            }

            string exportParentDirectory = SelectProtocolExportDirectory();
            if (string.IsNullOrWhiteSpace(exportParentDirectory))
            {
                return;
            }

            CancellationTokenSource cancellation = null;
            try
            {
                StreamingGenerationSession session = CreateStreamingGenerationSession(exportParentDirectory);
                Directory.CreateDirectory(session.ExportDirectory);

                cancellation = new CancellationTokenSource();
                _streamGenerationCancellation = cancellation;
                SetStreamingGenerationUiState(true, false);

                _streamGenerationTask = Task.Run(
                    () => RunStreamingGeneration(session, cancellation.Token));

                StreamingGenerationResult result = await _streamGenerationTask;
                MessageBox.Show(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "流式 BIN 生成已停止。{0}{0}目录: {1}{0}页数: {2:N0}{0}载荷字节数: {3:N0}{0}导出字节数: {4:N0}",
                        Environment.NewLine,
                        result.ExportDirectory,
                        result.PageCount,
                        result.PayloadByteCount,
                        result.ExportByteCount),
                    "已停止",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("流式 BIN 生成失败:\n" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (_streamGenerationCancellation == cancellation)
                {
                    _streamGenerationCancellation = null;
                    _streamGenerationTask = null;
                    SetStreamingGenerationUiState(false, false);
                }

                if (cancellation != null)
                {
                    cancellation.Dispose();
                }
            }
        }

        private void StopStreamingGeneration()
        {
            CancellationTokenSource cancellation = _streamGenerationCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested)
            {
                return;
            }

            cancellation.Cancel();
            SetStreamingGenerationUiState(true, true);
        }

        private void SetStreamingGenerationUiState(bool isStreaming, bool isStopping)
        {
            if (GenerateBinButton != null)
            {
                GenerateBinButton.IsEnabled = isStreaming == false;
            }

            if (StreamGenerateBinButton == null)
            {
                return;
            }

            StreamGenerateBinButton.Content = isStreaming
                ? (isStopping ? "正在停止..." : "停止")
                : "流式生成";
            StreamGenerateBinButton.IsEnabled = isStopping == false;
        }

        private StreamingGenerationSession CreateStreamingGenerationSession(string exportParentDirectory)
        {
            if (string.IsNullOrWhiteSpace(exportParentDirectory))
            {
                throw new InvalidOperationException("导出文件夹无效。");
            }

            uint sampleRate;
            if (TryGetSelectedSampleRate(out sampleRate) == false || sampleRate == 0)
            {
                throw new InvalidOperationException("采样率必须是有效值。");
            }

            SerialProtocolType protocolType = GetSelectedProtocolType();
            double pageDurationSeconds = GetPageDurationSeconds();
            byte emptyDataValue = GetEmptyDataValue();
            DateTime exportTimestamp = DateTime.Now;

            StreamingGenerationSession session = new StreamingGenerationSession
            {
                ProtocolType = protocolType,
                SampleRate = sampleRate,
                PageDurationSeconds = pageDurationSeconds,
                EmptyDataValue = emptyDataValue,
                EmptyDataRatio = GetEmptyDataRatio(),
                SeedBytes = Encoding.ASCII.GetBytes(PayloadAsciiTextBox.Text ?? string.Empty),
                RandomSeed = Environment.TickCount,
                ExportTimestamp = exportTimestamp
            };

            if (protocolType == SerialProtocolType.Uart)
            {
                session.BaudRate = GetSelectedBaudRate();
                session.SamplesPerBit = GetRoundedSamplesPerBit(sampleRate, session.BaudRate);
                session.DataBits = GetSelectedDataBits();
                session.StopBits = GetSelectedStopBits();
                session.ParityMode = GetSelectedParityMode();
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
            if (sampleRate % session.DataRate != 0)
            {
                throw new InvalidOperationException("采样率必须是数据速率的整数倍。");
            }

            session.SamplesPerBit = checked((int)(sampleRate / session.DataRate));
            session.LineCount = GetProtocolLineCount(protocolType);
            session.ExportDirectory = Path.Combine(
                exportParentDirectory,
                ProtocolBinNaming.BuildExportFolderName(session.LineCount, sampleRate, session.DataRate, exportTimestamp));
            return session;
        }

        private static StreamingGenerationResult RunStreamingGeneration(StreamingGenerationSession session, CancellationToken cancellationToken)
        {
            if (session == null)
            {
                throw new ArgumentNullException("session");
            }

            ProtocolPageManifest manifest = CreateStreamingManifest(session);
            StreamingGenerationResult result = new StreamingGenerationResult
            {
                ExportDirectory = session.ExportDirectory
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            long totalSamples = 0;

            while (cancellationToken.IsCancellationRequested == false)
            {
                int pageNumber = manifest.Pages.Count + 1;
                StreamingPageWriteResult pageResult = session.ProtocolType == SerialProtocolType.Uart
                    ? WriteStreamingUartPage(session, pageNumber, totalSamples)
                    : WriteStreamingProtocolPage(session, pageNumber, totalSamples);

                manifest.Pages.Add(pageResult.Page);
                ProtocolPageManifestStorage.Save(session.ExportDirectory, manifest);

                result.PageCount++;
                result.PayloadByteCount += pageResult.PayloadByteCount;
                result.ExportByteCount += pageResult.ExportByteCount;
                totalSamples += pageResult.Page.SampleCount;

                if (WaitForStreamingPace(stopwatch, totalSamples, session.SampleRate, cancellationToken))
                {
                    break;
                }
            }

            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        private static bool WaitForStreamingPace(Stopwatch stopwatch, long totalSamples, uint sampleRate, CancellationToken cancellationToken)
        {
            if (sampleRate == 0 || totalSamples <= 0)
            {
                return cancellationToken.IsCancellationRequested;
            }

            double targetMilliseconds = (totalSamples * 1000.0) / sampleRate;
            double remainingMilliseconds = targetMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
            if (remainingMilliseconds <= 1)
            {
                return cancellationToken.IsCancellationRequested;
            }

            int waitMilliseconds = remainingMilliseconds >= int.MaxValue ? int.MaxValue : (int)Math.Round(remainingMilliseconds);
            return cancellationToken.WaitHandle.WaitOne(Math.Max(1, waitMilliseconds));
        }

        private static ProtocolPageManifest CreateStreamingManifest(StreamingGenerationSession session)
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
                PageDurationSeconds = session.PageDurationSeconds,
                TimestampText = BuildManifestTimestampText(session.ExportTimestamp),
                Pages = new List<ProtocolPageManifestPage>()
            };
        }

        private static StreamingPageWriteResult WriteStreamingProtocolPage(
            StreamingGenerationSession session,
            int pageNumber,
            long startSampleIndex)
        {
            long payloadByteCount = GetStreamingProtocolPayloadByteCount(
                session.SampleRate,
                session.PageDurationSeconds,
                session.SamplesPerBit);
            byte[] payloadBytes = BuildLogicalPayloadBytes(
                session.SeedBytes,
                payloadByteCount,
                session.EmptyDataRatio,
                session.EmptyDataValue,
                session.RandomSeed + pageNumber,
                session.DataRate);
            byte[] payloadBytes2 = session.ProtocolType == SerialProtocolType.FourWireSerial
                ? BuildSecondaryLogicalPayloadBytes(payloadBytes, session.EmptyDataValue)
                : new byte[0];
            byte[] protocolBytes = BuildSampledProtocolBytes(
                payloadBytes,
                payloadBytes2,
                session.ProtocolType,
                GetClockValueForProtocol(session.ProtocolType),
                session.EmptyDataValue,
                session.SamplesPerBit);
            byte[][] channelBytes = ProtocolPageUtility.SplitInterleavedPackedBytes(protocolBytes, session.LineCount);
            if (channelBytes == null || channelBytes.Length != session.LineCount)
            {
                throw new InvalidOperationException("无法将流式协议数据拆分为通道。");
            }

            string fileName = ProtocolBinNaming.BuildPageFileName(pageNumber);
            byte[] outputBytes = ProtocolPageUtility.CombineChannelBytes(channelBytes);
            File.WriteAllBytes(Path.Combine(session.ExportDirectory, fileName), outputBytes);

            int sampleCount = checked((int)(payloadBytes.Length * (long)session.SamplesPerBit * 8));
            ProtocolPageManifestPage page = new ProtocolPageManifestPage
            {
                PageNumber = pageNumber,
                FileName = fileName,
                StartSampleIndex = startSampleIndex,
                SampleCount = sampleCount,
                ChannelCount = session.LineCount,
                BytesPerChannel = channelBytes.Length > 0 && channelBytes[0] != null ? channelBytes[0].Length : 0,
                LeadingHighSamples = 0,
                IsActiveData = ContainsNonDefaultData(
                    payloadBytes,
                    payloadBytes2,
                    session.ProtocolType,
                    0,
                    payloadBytes.Length,
                    session.EmptyDataValue),
                StartTimeSeconds = startSampleIndex / (double)session.SampleRate,
                DurationSeconds = sampleCount / (double)session.SampleRate,
                StartsOnBoundary = true,
                EndsOnBoundary = true
            };

            return new StreamingPageWriteResult
            {
                Page = page,
                PayloadByteCount = payloadBytes.Length,
                ExportByteCount = outputBytes.Length
            };
        }

        private static StreamingPageWriteResult WriteStreamingUartPage(
            StreamingGenerationSession session,
            int pageNumber,
            long startSampleIndex)
        {
            bool includeInitialIdle = pageNumber == 1;
            long payloadByteCount = GetStreamingUartPayloadByteCount(
                session.SampleRate,
                session.PageDurationSeconds,
                session.SamplesPerBit,
                session.DataBits,
                session.StopBits,
                session.ParityMode,
                includeInitialIdle);
            byte[] payloadBytes = BuildLogicalPayloadBytes(
                session.SeedBytes,
                payloadByteCount,
                session.EmptyDataRatio,
                session.EmptyDataValue,
                session.RandomSeed + pageNumber,
                (uint)Math.Max(1, session.BaudRate));
            int sampleCount;
            byte[] outputBytes = BuildStreamingUartPageBytes(
                payloadBytes,
                session.EmptyDataValue,
                session.SamplesPerBit,
                session.DataBits,
                session.StopBits,
                session.ParityMode,
                includeInitialIdle,
                out sampleCount);

            string fileName = ProtocolBinNaming.BuildPageFileName(pageNumber);
            File.WriteAllBytes(Path.Combine(session.ExportDirectory, fileName), outputBytes);

            ProtocolPageManifestPage page = new ProtocolPageManifestPage
            {
                PageNumber = pageNumber,
                FileName = fileName,
                StartSampleIndex = startSampleIndex,
                SampleCount = sampleCount,
                ChannelCount = 1,
                BytesPerChannel = outputBytes.Length,
                LeadingHighSamples = includeInitialIdle ? 0 : session.SamplesPerBit,
                IsActiveData = ContainsNonDefaultData(
                    payloadBytes,
                    null,
                    SerialProtocolType.Uart,
                    0,
                    payloadBytes.Length,
                    session.EmptyDataValue),
                StartTimeSeconds = startSampleIndex / (double)session.SampleRate,
                DurationSeconds = sampleCount / (double)session.SampleRate,
                StartsOnBoundary = true,
                EndsOnBoundary = true
            };

            return new StreamingPageWriteResult
            {
                Page = page,
                PayloadByteCount = payloadBytes.Length,
                ExportByteCount = outputBytes.Length
            };
        }

        private static long GetStreamingProtocolPayloadByteCount(uint sampleRate, double pageDurationSeconds, int samplesPerBit)
        {
            if (sampleRate == 0 || pageDurationSeconds <= 0 || samplesPerBit <= 0)
            {
                return 1;
            }

            long targetSamples = Math.Max(1L, (long)Math.Round(pageDurationSeconds * sampleRate, MidpointRounding.AwayFromZero));
            long samplesPerPayloadByte = (long)samplesPerBit * 8;
            if (samplesPerPayloadByte <= 0)
            {
                return 1;
            }

            return Math.Max(1, targetSamples / samplesPerPayloadByte);
        }

        private static long GetStreamingUartPayloadByteCount(
            uint sampleRate,
            double pageDurationSeconds,
            int samplesPerBit,
            int dataBits,
            double stopBits,
            UartParityMode parityMode,
            bool includeInitialIdle)
        {
            if (sampleRate == 0 || pageDurationSeconds <= 0 || samplesPerBit <= 0)
            {
                return 1;
            }

            long targetSamples = Math.Max(1L, (long)Math.Round(pageDurationSeconds * sampleRate, MidpointRounding.AwayFromZero));
            if (includeInitialIdle)
            {
                targetSamples -= samplesPerBit;
            }

            int frameSamples = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode);
            if (targetSamples <= 0 || frameSamples <= 0)
            {
                return 1;
            }

            return Math.Max(1, targetSamples / frameSamples);
        }

        private static byte[] BuildStreamingUartPageBytes(
            byte[] payloadBytes,
            byte idlePayloadValue,
            int samplesPerBit,
            int dataBits,
            double stopBits,
            UartParityMode parityMode,
            bool includeInitialIdle,
            out int sampleCount)
        {
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                sampleCount = 0;
                return new byte[0];
            }

            int stopSamples = GetStopBitSampleCount(samplesPerBit, stopBits);
            int parityBitCount = parityMode == UartParityMode.None ? 0 : 1;
            int frameSamples = samplesPerBit * (1 + dataBits + parityBitCount) + stopSamples;
            long totalSamples = checked((includeInitialIdle ? samplesPerBit : 0L) + ((long)payloadBytes.Length * frameSamples));
            if (totalSamples > int.MaxValue)
            {
                throw new InvalidOperationException("流式页采样点数过大，请缩短页时长或降低采样率。");
            }

            long outputLength = (totalSamples + 7) / 8;
            if (outputLength > int.MaxValue)
            {
                throw new InvalidOperationException("流式页导出 BIN 过大，请缩短页时长或降低采样率。");
            }

            byte[] outputBytes = new byte[(int)outputLength];
            int sampleIndex = 0;
            if (includeInitialIdle)
            {
                WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, samplesPerBit);
            }

            for (int byteIndex = 0; byteIndex < payloadBytes.Length; byteIndex++)
            {
                byte value = payloadBytes[byteIndex];
                if (value == idlePayloadValue)
                {
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, frameSamples);
                    continue;
                }

                WriteRepeatedUartBit(outputBytes, ref sampleIndex, false, samplesPerBit);
                for (int bitIndex = 0; bitIndex < dataBits; bitIndex++)
                {
                    bool bitHigh = bitIndex < 8 && ((value >> bitIndex) & 0x1) != 0;
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, bitHigh, samplesPerBit);
                }

                if (parityMode != UartParityMode.None)
                {
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, CalculateUartParityBit(value, dataBits, parityMode), samplesPerBit);
                }

                WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, stopSamples);
            }

            sampleCount = (int)totalSamples;
            FillRemainingUartIdleBits(outputBytes, ref sampleIndex);
            return outputBytes;
        }

        private static byte GetClockValueForProtocol(SerialProtocolType protocolType)
        {
            return IsClockedProtocol(protocolType) ? ProtocolClockBitPattern : (byte)0x00;
        }

        private void UpdatePayloadPreview()
        {
            if (PayloadAsciiTextBox == null || PayloadHexTextBox == null || PayloadSummaryTextBlock == null)
            {
                return;
            }

            string asciiText = PayloadAsciiTextBox.Text ?? string.Empty;
            byte[] asciiBytes = Encoding.ASCII.GetBytes(asciiText);

            PayloadHexTextBox.Text = FormatHexBytes(asciiBytes);
            PayloadSummaryTextBlock.Text = string.Format(
                "字符数: {0}    字节数: {1}",
                asciiText.Length,
                asciiBytes.Length);
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

        private double GetDurationSeconds()
        {
            string text = DurationSecondsTextBox == null ? null : DurationSecondsTextBox.Text;
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false
                && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                throw new InvalidOperationException("时长必须是有效的正数。");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException("时长必须大于 0。");
            }

            return value;
        }

        private double GetPageDurationSeconds()
        {
            string text = PageDurationSecondsTextBox == null ? null : PageDurationSecondsTextBox.Text;
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false
                && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                throw new InvalidOperationException("页时长必须是正数。");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException("页时长必须大于 0。");
            }

            return value;
        }

        private int GetEmptyDataRatio()
        {
            string text = EmptyDataRatioTextBox == null ? null : EmptyDataRatioTextBox.Text;
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == false
                && int.TryParse(text, out value) == false)
            {
                throw new InvalidOperationException("空数据占比必须是 0 到 100 之间的整数。");
            }

            if (value < 0 || value > 100)
            {
                throw new InvalidOperationException("空数据占比必须是 0 到 100 之间的整数。");
            }

            return value;
        }

        private byte GetEmptyDataValue()
        {
            return ParseByteValue(EmptyDataValueTextBox == null ? null : EmptyDataValueTextBox.Text, "空数据值");
        }

        private byte GetClockValue(SerialProtocolType protocolType)
        {
            return IsClockedProtocol(protocolType) ? ProtocolClockBitPattern : (byte)0x00;
        }

        private static bool IsClockedProtocol(SerialProtocolType protocolType)
        {
            return protocolType == SerialProtocolType.TwoWireSerial
                || protocolType == SerialProtocolType.ThreeWireSerial
                || protocolType == SerialProtocolType.FourWireSerial;
        }

        private static long GetLogicalDataByteCount(uint dataRate, double durationSeconds)
        {
            double byteCount = (dataRate * durationSeconds) / 8.0;
            if (byteCount <= 0)
            {
                return 1;
            }

            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("逻辑载荷过大，请缩短时长或降低数据速率。");
            }

            return Math.Max(1, (long)Math.Round(byteCount, MidpointRounding.AwayFromZero));
        }

        private static byte[] BuildLogicalPayloadBytes(byte[] seedBytes, long byteCount, int emptyDataRatio, byte emptyDataValue, int randomSeed, uint dataRate)
        {
            if (byteCount <= 0)
            {
                return new byte[0];
            }

            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("逻辑载荷过大。");
            }

            byte[] normalizedSeedBytes = seedBytes == null || seedBytes.Length == 0
                ? new[] { emptyDataValue }
                : seedBytes;
            byte[] outputBytes = new byte[(int)byteCount];
            bool[] emptyMask = BuildSegmentedEmptyMask(outputBytes.Length, emptyDataRatio, randomSeed, dataRate);
            int payloadIndex = 0;

            for (int i = 0; i < outputBytes.Length; i++)
            {
                if (emptyMask[i])
                {
                    outputBytes[i] = emptyDataValue;
                    continue;
                }

                outputBytes[i] = normalizedSeedBytes[payloadIndex % normalizedSeedBytes.Length];
                payloadIndex++;
            }

            return outputBytes;
        }

        private static bool[] BuildSegmentedEmptyMask(int byteCount, int emptyDataRatio, int randomSeed, uint dataRate)
        {
            bool[] emptyMask = new bool[byteCount];
            if (byteCount <= 0 || emptyDataRatio <= 0)
            {
                return emptyMask;
            }

            int emptyByteCount = (int)Math.Round(
                (((long)byteCount) * emptyDataRatio) / 100.0,
                MidpointRounding.AwayFromZero);
            emptyByteCount = Math.Max(0, Math.Min(byteCount, emptyByteCount));
            if (emptyByteCount == 0)
            {
                return emptyMask;
            }

            if (emptyByteCount >= byteCount)
            {
                for (int i = 0; i < emptyMask.Length; i++)
                {
                    emptyMask[i] = true;
                }

                return emptyMask;
            }

            int emptySegmentMinBytes = GetLogicalByteCountForDuration(dataRate, MinimumEmptySegmentDurationMilliseconds, byteCount);
            int emptySegmentMinGapBytes = GetLogicalByteCountForDuration(dataRate, MinimumEmptySegmentIntervalMilliseconds, byteCount);
            int nonEmptyByteCount = byteCount - emptyByteCount;
            Random random = new Random(randomSeed);
            int emptySegmentCount = DetermineEmptySegmentCount(
                emptyByteCount,
                nonEmptyByteCount,
                emptySegmentMinBytes,
                emptySegmentMinGapBytes);
            int[] emptySegmentLengths = BuildEmptySegmentLengths(
                emptyByteCount,
                emptySegmentCount,
                emptySegmentMinBytes,
                random);
            int[] gapLengths = BuildGapLengths(
                nonEmptyByteCount,
                emptySegmentCount,
                emptySegmentMinGapBytes,
                random);
            int index = gapLengths[0];

            for (int i = 0; i < emptySegmentLengths.Length; i++)
            {
                int segmentLength = emptySegmentLengths[i];
                for (int j = 0; j < segmentLength && index + j < emptyMask.Length; j++)
                {
                    emptyMask[index + j] = true;
                }

                index += segmentLength + gapLengths[i + 1];
            }

            return emptyMask;
        }

        private static int DetermineEmptySegmentCount(int emptyByteCount, int nonEmptyByteCount, int emptySegmentMinBytes, int emptySegmentMinGapBytes)
        {
            if (emptyByteCount <= 0)
            {
                return 0;
            }

            int maximumSegmentsByLength = Math.Max(1, emptyByteCount / Math.Max(1, emptySegmentMinBytes));
            int maximumSegmentsByGap = nonEmptyByteCount <= 0
                ? 1
                : Math.Max(1, (nonEmptyByteCount / Math.Max(1, emptySegmentMinGapBytes)) + 1);
            return Math.Max(1, Math.Min(maximumSegmentsByLength, maximumSegmentsByGap));
        }

        private static int[] BuildEmptySegmentLengths(int emptyByteCount, int emptySegmentCount, int emptySegmentMinBytes, Random random)
        {
            if (emptySegmentCount <= 1)
            {
                return new[] { emptyByteCount };
            }

            int[] lengths = new int[emptySegmentCount];
            int assignedBytes = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = emptySegmentMinBytes;
                assignedBytes += emptySegmentMinBytes;
            }

            int remainingBytes = Math.Max(0, emptyByteCount - assignedBytes);
            DistributeBytes(lengths, remainingBytes, int.MaxValue, random);

            ShuffleArray(lengths, random);
            return lengths;
        }

        private static int[] BuildGapLengths(int nonEmptyByteCount, int emptySegmentCount, int emptySegmentMinGapBytes, Random random)
        {
            int[] gapLengths = new int[Math.Max(1, emptySegmentCount + 1)];
            if (emptySegmentCount <= 0)
            {
                gapLengths[0] = nonEmptyByteCount;
                return gapLengths;
            }

            int internalGapCount = Math.Max(0, emptySegmentCount - 1);
            int requiredGapBytes = internalGapCount * emptySegmentMinGapBytes;
            for (int i = 1; i < emptySegmentCount; i++)
            {
                gapLengths[i] = emptySegmentMinGapBytes;
            }

            int remainingBytes = Math.Max(0, nonEmptyByteCount - requiredGapBytes);
            DistributeBytes(gapLengths, remainingBytes, int.MaxValue, random);
            return gapLengths;
        }

        private static int DistributeBytes(int[] lengths, int remainingBytes, int perSlotLimit, Random random)
        {
            if (lengths == null || lengths.Length == 0 || remainingBytes <= 0)
            {
                return remainingBytes;
            }

            while (remainingBytes > 0)
            {
                bool canGrowAnySlot = false;
                for (int i = 0; i < lengths.Length; i++)
                {
                    if (lengths[i] < perSlotLimit)
                    {
                        canGrowAnySlot = true;
                        break;
                    }
                }

                if (canGrowAnySlot == false)
                {
                    return remainingBytes;
                }

                int slotIndex = random.Next(lengths.Length);
                if (lengths[slotIndex] >= perSlotLimit)
                {
                    continue;
                }

                int maxGrowth = perSlotLimit == int.MaxValue
                    ? Math.Max(1, remainingBytes / lengths.Length)
                    : perSlotLimit - lengths[slotIndex];
                maxGrowth = Math.Max(1, Math.Min(remainingBytes, maxGrowth));
                int growth = maxGrowth == 1 ? 1 : random.Next(1, maxGrowth + 1);
                lengths[slotIndex] += growth;
                remainingBytes -= growth;
            }

            return 0;
        }

        private static int GetLogicalByteCountForDuration(uint dataRate, double durationMilliseconds, int byteCountLimit)
        {
            if (byteCountLimit <= 0)
            {
                return 1;
            }

            long byteCount = GetLogicalDataByteCount(dataRate, durationMilliseconds / 1000.0);
            if (byteCount <= 0)
            {
                return 1;
            }

            return (int)Math.Max(1, Math.Min((long)byteCountLimit, byteCount));
        }

        private static void ShuffleArray(int[] values, Random random)
        {
            if (values == null || values.Length <= 1)
            {
                return;
            }

            for (int i = values.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                int temp = values[i];
                values[i] = values[swapIndex];
                values[swapIndex] = temp;
            }
        }

        private static byte[] BuildSecondaryLogicalPayloadBytes(byte[] primaryBytes, byte emptyDataValue)
        {
            if (primaryBytes == null || primaryBytes.Length == 0)
            {
                return new byte[0];
            }

            byte[] secondaryBytes = new byte[primaryBytes.Length];
            for (int i = 0; i < primaryBytes.Length; i++)
            {
                secondaryBytes[i] = primaryBytes[i] == emptyDataValue
                    ? emptyDataValue
                    : RotateByteLeft(primaryBytes[i], 1);
            }

            return secondaryBytes;
        }

        private static int GetRoundedSamplesPerBit(uint sampleRate, int baudRate)
        {
            if (sampleRate == 0 || baudRate <= 0)
            {
                throw new InvalidOperationException("采样率和波特率必须为正数。");
            }

            double roundedSamplesPerBit = Math.Round(sampleRate / (double)baudRate, MidpointRounding.AwayFromZero);
            if (roundedSamplesPerBit <= 0 || roundedSamplesPerBit > int.MaxValue)
            {
                throw new InvalidOperationException("每位重复采样点数超出范围。");
            }

            return (int)roundedSamplesPerBit;
        }

        private static long GetUartPayloadByteCount(uint sampleRate, double durationSeconds, int samplesPerBit, int dataBits, double stopBits, UartParityMode parityMode)
        {
            if (sampleRate == 0 || durationSeconds <= 0 || samplesPerBit <= 0)
            {
                return 0;
            }

            long totalSamples = (long)Math.Round(sampleRate * durationSeconds, MidpointRounding.AwayFromZero);
            int frameSamples = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode);
            long availableSamples = totalSamples - (2L * samplesPerBit);
            if (availableSamples <= 0 || frameSamples <= 0)
            {
                return 1;
            }

            return Math.Max(1, availableSamples / frameSamples);
        }

        private static int GetUartFrameSampleCount(int samplesPerBit, int dataBits, double stopBits, UartParityMode parityMode)
        {
            int parityBitCount = parityMode == UartParityMode.None ? 0 : 1;
            int stopSamples = GetStopBitSampleCount(samplesPerBit, stopBits);
            return checked(samplesPerBit * (1 + dataBits + parityBitCount) + stopSamples);
        }

        private static int GetStopBitSampleCount(int samplesPerBit, double stopBits)
        {
            int stopSamples = (int)Math.Round(samplesPerBit * stopBits, MidpointRounding.AwayFromZero);
            return Math.Max(1, stopSamples);
        }

        private static byte[] BuildSampledUartBytes(byte[] payloadBytes, byte idlePayloadValue, int samplesPerBit, int dataBits, double stopBits, UartParityMode parityMode)
        {
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return new byte[0];
            }

            int stopSamples = GetStopBitSampleCount(samplesPerBit, stopBits);
            int parityBitCount = parityMode == UartParityMode.None ? 0 : 1;
            int frameSamples = samplesPerBit * (1 + dataBits + parityBitCount) + stopSamples;
            long totalSamples = checked(
                (2L * samplesPerBit) +
                ((long)payloadBytes.Length * frameSamples));
            long outputLength = (totalSamples + 7) / 8;
            if (outputLength > int.MaxValue)
            {
                throw new InvalidOperationException("导出的 BIN 过大，请缩短时长或降低采样率。");
            }

            byte[] outputBytes = new byte[(int)outputLength];
            int sampleIndex = 0;
            WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, samplesPerBit);

            for (int byteIndex = 0; byteIndex < payloadBytes.Length; byteIndex++)
            {
                byte value = payloadBytes[byteIndex];
                if (value == idlePayloadValue)
                {
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, frameSamples);
                    continue;
                }

                WriteRepeatedUartBit(outputBytes, ref sampleIndex, false, samplesPerBit);
                for (int bitIndex = 0; bitIndex < dataBits; bitIndex++)
                {
                    bool bitHigh = bitIndex < 8 && ((value >> bitIndex) & 0x1) != 0;
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, bitHigh, samplesPerBit);
                }

                if (parityMode != UartParityMode.None)
                {
                    WriteRepeatedUartBit(outputBytes, ref sampleIndex, CalculateUartParityBit(value, dataBits, parityMode), samplesPerBit);
                }

                WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, stopSamples);
            }

            WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, samplesPerBit);
            FillRemainingUartIdleBits(outputBytes, ref sampleIndex);
            return outputBytes;
        }

        private static void FillRemainingUartIdleBits(byte[] outputBytes, ref int sampleIndex)
        {
            int totalSampleCapacity = outputBytes.Length * 8;
            if (sampleIndex < totalSampleCapacity)
            {
                WriteRepeatedUartBit(outputBytes, ref sampleIndex, true, totalSampleCapacity - sampleIndex);
            }
        }

        private static void WriteRepeatedUartBit(byte[] outputBytes, ref int sampleIndex, bool bitHigh, int repeatCount)
        {
            for (int i = 0; i < repeatCount; i++)
            {
                if (bitHigh)
                {
                    int byteIndex = sampleIndex / 8;
                    int bitOffset = 7 - (sampleIndex % 8);
                    outputBytes[byteIndex] |= (byte)(1 << bitOffset);
                }

                sampleIndex++;
            }
        }

        private static bool CalculateUartParityBit(byte value, int dataBits, UartParityMode parityMode)
        {
            switch (parityMode)
            {
                case UartParityMode.Odd:
                    return (CountSetBits(value, dataBits) & 0x1) == 0;
                case UartParityMode.Even:
                    return (CountSetBits(value, dataBits) & 0x1) != 0;
                case UartParityMode.Mark:
                    return true;
                case UartParityMode.Space:
                    return false;
                default:
                    return false;
            }
        }

        private static int CountSetBits(byte value, int dataBits)
        {
            int count = 0;
            for (int bitIndex = 0; bitIndex < dataBits; bitIndex++)
            {
                if (bitIndex < 8 && ((value >> bitIndex) & 0x1) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static byte[] BuildSampledProtocolBytes(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            byte clockValue,
            byte defaultByteValue,
            int samplesPerBit)
        {
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return new byte[0];
            }

            if (samplesPerBit <= 0)
            {
                throw new InvalidOperationException("每位重复采样点数必须大于 0。");
            }

            int lineCount = GetProtocolLineCount(protocolType);
            long outputLength = checked((long)payloadBytes.Length * samplesPerBit * lineCount);
            if (outputLength > int.MaxValue)
            {
                throw new InvalidOperationException("导出的 BIN 数据过大，请缩短时长或降低采样率。");
            }

            byte[] exportBytes = new byte[(int)outputLength];
            byte[][] expandedByteCache = new byte[256][];
            byte[] expandedClock = GetExpandedBytePattern(clockValue, samplesPerBit, expandedByteCache);
            byte[] expandedDefault = GetExpandedBytePattern(defaultByteValue, samplesPerBit, expandedByteCache);
            int outputIndex = 0;

            for (int payloadIndex = 0; payloadIndex < payloadBytes.Length; payloadIndex++)
            {
                byte[] expandedData = GetExpandedBytePattern(payloadBytes[payloadIndex], samplesPerBit, expandedByteCache);
                byte[] expandedData2 = payloadBytes2 != null && payloadIndex < payloadBytes2.Length
                    ? GetExpandedBytePattern(payloadBytes2[payloadIndex], samplesPerBit, expandedByteCache)
                    : expandedDefault;
                byte[] expandedEnable = GetExpandedBytePattern(
                    GetProtocolEnableValue(payloadBytes[payloadIndex], payloadBytes2, payloadIndex, protocolType, defaultByteValue),
                    samplesPerBit,
                    expandedByteCache);

                for (int packedByteIndex = 0; packedByteIndex < samplesPerBit; packedByteIndex++)
                {
                    switch (protocolType)
                    {
                        case SerialProtocolType.TwoWireSerial:
                            exportBytes[outputIndex++] = expandedClock[packedByteIndex];
                            exportBytes[outputIndex++] = expandedData[packedByteIndex];
                            break;
                        case SerialProtocolType.ThreeWireSerial:
                            exportBytes[outputIndex++] = expandedClock[packedByteIndex];
                            exportBytes[outputIndex++] = expandedEnable[packedByteIndex];
                            exportBytes[outputIndex++] = expandedData[packedByteIndex];
                            break;
                        case SerialProtocolType.FourWireSerial:
                            exportBytes[outputIndex++] = expandedClock[packedByteIndex];
                            exportBytes[outputIndex++] = expandedEnable[packedByteIndex];
                            exportBytes[outputIndex++] = expandedData[packedByteIndex];
                            exportBytes[outputIndex++] = expandedData2[packedByteIndex];
                            break;
                        default:
                            throw new InvalidOperationException("不支持的协议类型。");
                    }
                }
            }

            return exportBytes;
        }

        private static byte GetProtocolEnableValue(
            byte dataValue,
            byte[] payloadBytes2,
            int payloadIndex,
            SerialProtocolType protocolType,
            byte defaultByteValue)
        {
            bool hasData = dataValue != defaultByteValue;
            if (protocolType == SerialProtocolType.FourWireSerial)
            {
                byte dataValue2 = payloadBytes2 != null && payloadIndex < payloadBytes2.Length
                    ? payloadBytes2[payloadIndex]
                    : defaultByteValue;
                hasData = hasData || dataValue2 != defaultByteValue;
            }

            return hasData ? (byte)0x00 : (byte)0xFF;
        }

        private static byte[] GetExpandedBytePattern(byte value, int samplesPerBit, byte[][] expandedByteCache)
        {
            byte[] cachedPattern = expandedByteCache[value];
            if (cachedPattern != null && cachedPattern.Length == samplesPerBit)
            {
                return cachedPattern;
            }

            byte[] pattern = new byte[samplesPerBit];
            int totalExpandedSamples = samplesPerBit * 8;
            for (int expandedSampleIndex = 0; expandedSampleIndex < totalExpandedSamples; expandedSampleIndex++)
            {
                int sourceBitIndex = expandedSampleIndex / samplesPerBit;
                int sourceBitOffset = 7 - sourceBitIndex;
                if (((value >> sourceBitOffset) & 0x1) == 0)
                {
                    continue;
                }

                int packedByteIndex = expandedSampleIndex / 8;
                int packedBitOffset = 7 - (expandedSampleIndex % 8);
                pattern[packedByteIndex] |= (byte)(1 << packedBitOffset);
            }

            expandedByteCache[value] = pattern;
            return pattern;
        }

        private static int WriteProtocolPages(
            byte[] protocolBytes,
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            int lineCount,
            uint sampleRate,
            uint dataRate,
            int samplesPerBit,
            byte emptyDataValue,
            double pageDurationSeconds,
            DateTime exportTimestamp,
            string exportDirectory)
        {
            byte[][] channelBytes = ProtocolPageUtility.SplitInterleavedPackedBytes(protocolBytes, lineCount);
            if (channelBytes == null || channelBytes.Length != lineCount)
            {
                throw new InvalidOperationException("无法将协议数据拆分为各通道分页。");
            }

            int totalSamples = checked(channelBytes[0].Length * 8);
            List<ProtocolPageManifestPage> pages = ProtocolPageUtility.BuildFixedWidthPages(
                totalSamples,
                samplesPerBit * 8,
                sampleRate,
                pageDurationSeconds,
                lineCount);
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("未生成任何协议分页。");
            }

            UpdateFixedWidthPageActivity(pages, payloadBytes, payloadBytes2, protocolType, emptyDataValue, samplesPerBit);

            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                ProtocolPageManifestPage page = pages[pageIndex];
                byte[][] pageChannelBytes = new byte[lineCount][];
                for (int channelIndex = 0; channelIndex < lineCount; channelIndex++)
                {
                    pageChannelBytes[channelIndex] = ProtocolPageUtility.ExtractPackedBits(
                        channelBytes[channelIndex],
                        page.StartSampleIndex,
                        page.SampleCount);
                }

                byte[] outputBytes = ProtocolPageUtility.CombineChannelBytes(pageChannelBytes);
                File.WriteAllBytes(Path.Combine(exportDirectory, page.FileName), outputBytes);
            }

            ProtocolPageManifestStorage.Save(
                exportDirectory,
                new ProtocolPageManifest
                {
                    Version = 1,
                    ProtocolType = GetProtocolTypeFromLineCount(lineCount).ToString(),
                    LineCount = lineCount,
                    SampleRate = sampleRate,
                    DataRate = dataRate,
                    BaudRate = 0,
                    ParityText = string.Empty,
                    DataBits = 8,
                    StopBits = 0,
                    SamplesPerBit = samplesPerBit,
                    PageDurationSeconds = pageDurationSeconds,
                    TimestampText = BuildManifestTimestampText(exportTimestamp),
                    Pages = pages
                });

            return pages.Count;
        }

        private static int WriteUartPages(
            byte[] uartBytes,
            byte[] payloadBytes,
            uint sampleRate,
            int baudRate,
            UartParityMode parityMode,
            int dataBits,
            double stopBits,
            int samplesPerBit,
            byte emptyDataValue,
            double pageDurationSeconds,
            DateTime exportTimestamp,
            string exportDirectory)
        {
            int frameSamples = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode);
            int totalSamples = GetTotalUartSampleCount(payloadBytes == null ? 0 : payloadBytes.Length, samplesPerBit, dataBits, stopBits, parityMode);
            List<ProtocolPageManifestPage> pages = ProtocolPageUtility.BuildUartPages(
                totalSamples,
                samplesPerBit,
                frameSamples,
                payloadBytes == null ? 0 : payloadBytes.Length,
                sampleRate,
                pageDurationSeconds);
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("未生成任何串口分页。");
            }

            UpdateUartPageActivity(pages, payloadBytes, emptyDataValue, samplesPerBit, dataBits, stopBits, parityMode);

            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                ProtocolPageManifestPage page = pages[pageIndex];
                byte[] pageBytes = ProtocolPageUtility.ExtractPackedBits(uartBytes, page.StartSampleIndex, page.SampleCount);
                File.WriteAllBytes(Path.Combine(exportDirectory, page.FileName), pageBytes);
            }

            ProtocolPageManifestStorage.Save(
                exportDirectory,
                new ProtocolPageManifest
                {
                    Version = 1,
                    ProtocolType = SerialProtocolType.Uart.ToString(),
                    LineCount = 1,
                    SampleRate = sampleRate,
                    DataRate = 0,
                    BaudRate = Math.Max(0, baudRate),
                    ParityText = parityMode.ToString(),
                    DataBits = dataBits,
                    StopBits = stopBits,
                    SamplesPerBit = samplesPerBit,
                    PageDurationSeconds = pageDurationSeconds,
                    TimestampText = BuildManifestTimestampText(exportTimestamp),
                    Pages = pages
                });

            return pages.Count;
        }

        private static void UpdateFixedWidthPageActivity(
            List<ProtocolPageManifestPage> pages,
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            byte emptyDataValue,
            int samplesPerBit)
        {
            if (pages == null || pages.Count == 0 || samplesPerBit <= 0)
            {
                return;
            }

            long samplesPerPayloadByte = (long)samplesPerBit * 8;
            if (samplesPerPayloadByte <= 0)
            {
                return;
            }

            for (int i = 0; i < pages.Count; i++)
            {
                ProtocolPageManifestPage page = pages[i];
                if (page == null)
                {
                    continue;
                }

                int startByteIndex = SafeLongToInt(page.StartSampleIndex / samplesPerPayloadByte);
                int endByteIndex = SafeLongToInt((page.StartSampleIndex + page.SampleCount) / samplesPerPayloadByte);
                page.IsActiveData = ContainsNonDefaultData(
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    startByteIndex,
                    endByteIndex,
                    emptyDataValue);
            }
        }

        private static void UpdateUartPageActivity(
            List<ProtocolPageManifestPage> pages,
            byte[] payloadBytes,
            byte emptyDataValue,
            int samplesPerBit,
            int dataBits,
            double stopBits,
            UartParityMode parityMode)
        {
            if (pages == null || pages.Count == 0 || samplesPerBit <= 0)
            {
                return;
            }

            int frameSamples = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode);
            if (frameSamples <= 0)
            {
                return;
            }

            for (int i = 0; i < pages.Count; i++)
            {
                ProtocolPageManifestPage page = pages[i];
                if (page == null)
                {
                    continue;
                }

                int startByteIndex = GetUartPageStartByteIndex(page.StartSampleIndex, samplesPerBit, frameSamples);
                int endByteIndex = GetUartPageEndByteIndex(page.StartSampleIndex + page.SampleCount, samplesPerBit, frameSamples);
                page.IsActiveData = ContainsNonDefaultData(
                    payloadBytes,
                    null,
                    SerialProtocolType.Uart,
                    startByteIndex,
                    endByteIndex,
                    emptyDataValue);
            }
        }

        private static int GetUartPageStartByteIndex(long startSampleIndex, int samplesPerBit, int frameSamples)
        {
            if (startSampleIndex <= samplesPerBit)
            {
                return 0;
            }

            return SafeLongToInt((startSampleIndex - samplesPerBit) / frameSamples);
        }

        private static int GetUartPageEndByteIndex(long endSampleIndexExclusive, int samplesPerBit, int frameSamples)
        {
            if (endSampleIndexExclusive <= samplesPerBit)
            {
                return 0;
            }

            long relativeSampleCount = endSampleIndexExclusive - samplesPerBit;
            return SafeLongToInt((relativeSampleCount + frameSamples - 1) / frameSamples);
        }

        private static int SafeLongToInt(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static int GetTotalUartSampleCount(int payloadByteCount, int samplesPerBit, int dataBits, double stopBits, UartParityMode parityMode)
        {
            if (payloadByteCount <= 0)
            {
                return 0;
            }

            int frameSamples = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode);
            long totalSamples = checked((2L * samplesPerBit) + ((long)payloadByteCount * frameSamples));
            if (totalSamples > int.MaxValue)
            {
                throw new InvalidOperationException("串口采样点数过大。");
            }

            return (int)totalSamples;
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

        private static SerialProtocolType GetProtocolTypeFromLineCount(int lineCount)
        {
            switch (lineCount)
            {
                case 2:
                    return SerialProtocolType.TwoWireSerial;
                case 3:
                    return SerialProtocolType.ThreeWireSerial;
                case 4:
                    return SerialProtocolType.FourWireSerial;
                default:
                    return SerialProtocolType.Uart;
            }
        }

        private static int WriteProtocolChunks(
            byte[] protocolBytes,
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            int lineCount,
            uint sampleRate,
            uint dataRate,
            double durationSeconds,
            byte emptyDataValue,
            string exportDirectory)
        {
            int bytesPerChunk = GetInterleavedProtocolByteCount(lineCount, sampleRate, ProtocolExportChunkSeconds);
            if (bytesPerChunk <= 0)
            {
                throw new InvalidOperationException("分页块大小无效。");
            }

            int chunkCount = Math.Max(1, (int)Math.Ceiling(protocolBytes.Length / (double)bytesPerChunk));
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int offset = chunkIndex * bytesPerChunk;
                int byteCount = Math.Min(bytesPerChunk, protocolBytes.Length - offset);
                if (byteCount <= 0)
                {
                    continue;
                }

                byte[] chunkBytes = new byte[byteCount];
                Buffer.BlockCopy(protocolBytes, offset, chunkBytes, 0, byteCount);
                double chunkStartSeconds = chunkIndex * ProtocolExportChunkSeconds;
                double chunkEndSeconds = Math.Min(durationSeconds, (chunkIndex + 1) * ProtocolExportChunkSeconds);
                string activePartitions = BuildActivePartitionList(
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    dataRate,
                    chunkStartSeconds,
                    chunkEndSeconds,
                    emptyDataValue);
                string fileName = ProtocolBinNaming.BuildExportChunkFileName(chunkIndex + 1, activePartitions);
                File.WriteAllBytes(Path.Combine(exportDirectory, fileName), chunkBytes);
            }

            return chunkCount;
        }

        private static int WriteUartChunks(
            byte[] uartBytes,
            byte[] payloadBytes,
            uint sampleRate,
            UartParityMode parityMode,
            int dataBits,
            double stopBits,
            int samplesPerBit,
            double durationSeconds,
            byte emptyDataValue,
            string exportDirectory)
        {
            int bytesPerChunk = GetPackedSampleByteCount(sampleRate, ProtocolExportChunkSeconds);
            if (bytesPerChunk <= 0)
            {
                throw new InvalidOperationException("串口分页块大小无效。");
            }

            int chunkCount = Math.Max(1, (int)Math.Ceiling(uartBytes.Length / (double)bytesPerChunk));
            double exportedDurationSeconds = sampleRate == 0 ? durationSeconds : (uartBytes.Length * 8.0) / sampleRate;
            double totalDurationSeconds = Math.Max(durationSeconds, exportedDurationSeconds);
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int offset = chunkIndex * bytesPerChunk;
                int byteCount = Math.Min(bytesPerChunk, uartBytes.Length - offset);
                if (byteCount <= 0)
                {
                    continue;
                }

                byte[] chunkBytes = new byte[byteCount];
                Buffer.BlockCopy(uartBytes, offset, chunkBytes, 0, byteCount);
                double chunkStartSeconds = chunkIndex * ProtocolExportChunkSeconds;
                double chunkEndSeconds = Math.Min(totalDurationSeconds, (chunkIndex + 1) * ProtocolExportChunkSeconds);
                string activePartitions = BuildUartActivePartitionList(
                    payloadBytes,
                    emptyDataValue,
                    sampleRate,
                    samplesPerBit,
                    dataBits,
                    stopBits,
                    parityMode,
                    chunkStartSeconds,
                    chunkEndSeconds);
                string fileName = ProtocolBinNaming.BuildExportChunkFileName(chunkIndex + 1, activePartitions);
                File.WriteAllBytes(Path.Combine(exportDirectory, fileName), chunkBytes);
            }

            return chunkCount;
        }

        private static string BuildActivePartitionList(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            uint dataRate,
            double chunkStartSeconds,
            double chunkEndSeconds,
            byte emptyDataValue)
        {
            int partitionCount = GetPartitionCountForChunk(chunkStartSeconds, chunkEndSeconds);
            StringBuilder builder = new StringBuilder();
            for (int partitionIndex = 0; partitionIndex < partitionCount; partitionIndex++)
            {
                double partitionStartSeconds = chunkStartSeconds + (partitionIndex * ProtocolExportPartitionDurationSeconds);
                double partitionEndSeconds = Math.Min(chunkEndSeconds, partitionStartSeconds + ProtocolExportPartitionDurationSeconds);
                int startByteIndex = GetLogicalByteIndexAtTime(dataRate, partitionStartSeconds);
                int endByteIndex = GetLogicalByteIndexAtTime(dataRate, partitionEndSeconds);
                if (ContainsNonDefaultData(payloadBytes, payloadBytes2, protocolType, startByteIndex, endByteIndex, emptyDataValue) == false)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(',');
                }

                builder.Append(partitionIndex + 1);
            }

            return builder.ToString();
        }

        private static string BuildUartActivePartitionList(
            byte[] payloadBytes,
            byte emptyDataValue,
            uint sampleRate,
            int samplesPerBit,
            int dataBits,
            double stopBits,
            UartParityMode parityMode,
            double chunkStartSeconds,
            double chunkEndSeconds)
        {
            int partitionCount = GetPartitionCountForChunk(chunkStartSeconds, chunkEndSeconds);
            StringBuilder builder = new StringBuilder();
            for (int partitionIndex = 0; partitionIndex < partitionCount; partitionIndex++)
            {
                double partitionStartSeconds = chunkStartSeconds + (partitionIndex * ProtocolExportPartitionDurationSeconds);
                double partitionEndSeconds = Math.Min(chunkEndSeconds, partitionStartSeconds + ProtocolExportPartitionDurationSeconds);
                int startByteIndex = GetUartPayloadByteIndexAtTime(sampleRate, samplesPerBit, dataBits, stopBits, parityMode, partitionStartSeconds, false);
                int endByteIndex = GetUartPayloadByteIndexAtTime(sampleRate, samplesPerBit, dataBits, stopBits, parityMode, partitionEndSeconds, true);
                if (ContainsNonDefaultData(payloadBytes, null, SerialProtocolType.Uart, startByteIndex, endByteIndex, emptyDataValue) == false)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(',');
                }

                builder.Append(partitionIndex + 1);
            }

            return builder.ToString();
        }

        private static bool ContainsNonDefaultData(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            int startByteIndex,
            int endByteIndex,
            byte emptyDataValue)
        {
            int startIndex = Math.Max(0, startByteIndex);
            int endIndex = Math.Max(startIndex, endByteIndex);
            for (int i = startIndex; i < endIndex; i++)
            {
                if (payloadBytes != null && i < payloadBytes.Length && payloadBytes[i] != emptyDataValue)
                {
                    return true;
                }

                if (protocolType == SerialProtocolType.FourWireSerial
                    && payloadBytes2 != null
                    && i < payloadBytes2.Length
                    && payloadBytes2[i] != emptyDataValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetUartPayloadByteIndexAtTime(
            uint sampleRate,
            int samplesPerBit,
            int dataBits,
            double stopBits,
            UartParityMode parityMode,
            double timeSeconds,
            bool roundUp)
        {
            if (timeSeconds <= 0 || sampleRate == 0 || samplesPerBit <= 0)
            {
                return 0;
            }

            double initialIdleSeconds = samplesPerBit / (double)sampleRate;
            double frameDurationSeconds = GetUartFrameSampleCount(samplesPerBit, dataBits, stopBits, parityMode) / (double)sampleRate;
            if (frameDurationSeconds <= 0 || timeSeconds <= initialIdleSeconds)
            {
                return 0;
            }

            double byteIndex = (timeSeconds - initialIdleSeconds) / frameDurationSeconds;
            if (byteIndex >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(0, roundUp ? (int)Math.Ceiling(byteIndex) : (int)Math.Floor(byteIndex));
        }

        private static int GetLogicalByteIndexAtTime(uint dataRate, double timeSeconds)
        {
            if (timeSeconds <= 0 || dataRate == 0)
            {
                return 0;
            }

            double byteIndex = (dataRate * timeSeconds) / 8.0;
            if (byteIndex >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(0, (int)Math.Ceiling(byteIndex));
        }

        private static int GetPartitionCountForChunk(double chunkStartSeconds, double chunkEndSeconds)
        {
            double chunkDuration = Math.Max(0, chunkEndSeconds - chunkStartSeconds);
            int partitionCount = (int)Math.Ceiling(chunkDuration / ProtocolExportPartitionDurationSeconds);
            return Math.Max(1, Math.Min(ProtocolExportPartitionsPerChunk, partitionCount));
        }

        private static int GetInterleavedProtocolByteCount(int lineCount, uint sampleRate, double durationSeconds)
        {
            if (lineCount <= 0 || sampleRate == 0 || durationSeconds <= 0)
            {
                return 0;
            }

            double byteCount = (sampleRate * lineCount * durationSeconds) / 8.0;
            if (byteCount <= 0 || byteCount > int.MaxValue)
            {
                return 0;
            }

            return (int)Math.Round(byteCount, MidpointRounding.AwayFromZero);
        }

        private static int GetPackedSampleByteCount(uint sampleRate, double durationSeconds)
        {
            if (sampleRate == 0 || durationSeconds <= 0)
            {
                return 0;
            }

            double byteCount = (sampleRate * durationSeconds) / 8.0;
            if (byteCount <= 0 || byteCount > int.MaxValue)
            {
                return 0;
            }

            return (int)Math.Round(byteCount, MidpointRounding.AwayFromZero);
        }

        private static byte RotateByteLeft(byte value, int count)
        {
            int normalizedCount = count & 7;
            return (byte)((value << normalizedCount) | (value >> (8 - normalizedCount)));
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

        private static byte ParseByteValue(string text, string fieldName)
        {
            string normalizedText = string.IsNullOrWhiteSpace(text) ? "0" : text.Trim();
            bool parseAsHex = normalizedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                || normalizedText.IndexOfAny(new[] { 'A', 'B', 'C', 'D', 'E', 'F', 'a', 'b', 'c', 'd', 'e', 'f' }) >= 0;
            if (normalizedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalizedText = normalizedText.Substring(2);
            }

            byte value;
            if (parseAsHex)
            {
                if (byte.TryParse(normalizedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    return value;
                }
            }
            else
            {
                if (byte.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                    || byte.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
                {
                    return value;
                }

                if (byte.TryParse(normalizedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    return value;
                }
            }

            throw new InvalidOperationException(fieldName + " 必须是 0-255 或 00-FF 范围内的字节值。");
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

        private static string GetProtocolDisplayName(SerialProtocolType protocolType)
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
                    return "串口";
            }
        }

        private static string SelectProtocolExportDirectory()
        {
            using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "选择导出协议 BIN 文件的文件夹。";
                folderDialog.ShowNewFolderButton = true;
                return folderDialog.ShowDialog() == Forms.DialogResult.OK
                    ? folderDialog.SelectedPath
                    : null;
            }
        }

        private static string FormatHexBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    if (i % HexBytesPerLine == 0)
                    {
                        builder.AppendLine();
                    }
                    else
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(bytes[i].ToString("X2"));
            }

            return builder.ToString();
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

