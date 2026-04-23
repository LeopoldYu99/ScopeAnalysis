using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using InteractiveExamples;
using Forms = System.Windows.Forms;

namespace LCWpf
{
    public partial class BinFileDataProducer : UserControl
    {
        private const int HexBytesPerLine = 16;
        private const int ProtocolExportChunkSeconds = 5;
        private const double ProtocolExportPartitionDurationSeconds = 0.1;
        private const int ProtocolExportPartitionsPerChunk = (int)(ProtocolExportChunkSeconds / ProtocolExportPartitionDurationSeconds);
        private const double MinimumEmptySegmentDurationMilliseconds = 20;
        private const double MinimumEmptySegmentIntervalMilliseconds = 40;

        public BinFileDataProducer()
        {
            InitializeComponent();

            PayloadAsciiTextBox.TextChanged += PayloadAsciiTextBox_TextChanged;
            ProtocolTypeComboBox.SelectionChanged += ProtocolTypeComboBox_SelectionChanged;
            SampleRateComboBox.SelectionChanged += SettingsAffectingBitIntervalChanged;

            SelectComboBoxItemByText(SampleRateComboBox, "50M");
            SelectComboBoxItemByText(BaudRateComboBox, "115200");
            SelectComboBoxItemByText(ParityComboBox, "None");
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
                    MessageBox.Show(
                        "当前阶段先完成 2/3/4 线串口协议 BIN 生成，普通串口生成稍后再接入。",
                        "暂未支持",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
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
                    throw new InvalidOperationException("采样率必须是数据速率的整数倍，才能按整数采样点重复每个 bit。");
                }

                int samplesPerBit = checked((int)(sampleRate / dataRate));
                double durationSeconds = GetDurationSeconds();
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
                    GetEnableValue(protocolType),
                    emptyDataValue,
                    samplesPerBit);

                if (protocolBytes.Length == 0)
                {
                    throw new InvalidOperationException("没有生成任何协议数据。");
                }

                string exportParentDirectory = SelectProtocolExportDirectory();
                if (string.IsNullOrWhiteSpace(exportParentDirectory))
                {
                    return;
                }

                int lineCount = GetProtocolLineCount(protocolType);
                string exportDirectory = Path.Combine(
                    exportParentDirectory,
                    ProtocolBinNaming.BuildExportFolderName(lineCount, sampleRate, dataRate, DateTime.Now));
                Directory.CreateDirectory(exportDirectory);

                int chunkCount = WriteProtocolChunks(
                    protocolBytes,
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    lineCount,
                    sampleRate,
                    dataRate,
                    durationSeconds,
                    emptyDataValue,
                    exportDirectory);

                MessageBox.Show(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "BIN 生成完成。{0}{0}协议: {1}{0}目录: {2}{0}文件数: {3:N0}{0}采样率: {4:N0} Hz{0}数据速率: {5:N0} bps{0}每 bit 重复采样点: {6:N0}{0}逻辑数据字节: {7:N0}{0}导出字节: {8:N0}",
                        Environment.NewLine,
                        GetProtocolDisplayName(protocolType),
                        exportDirectory,
                        chunkCount,
                        sampleRate,
                        dataRate,
                        samplesPerBit,
                        payloadBytes.Length,
                        protocolBytes.Length),
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("BIN 生成失败:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                "Characters: {0}    ASCII bytes: {1}",
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

        private double GetDurationSeconds()
        {
            string text = DurationSecondsTextBox == null ? null : DurationSecondsTextBox.Text;
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) == false
                && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) == false)
            {
                throw new InvalidOperationException("文件时长必须是正数。");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException("文件时长必须大于 0。");
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
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return ParseByteValue(TwoWireClkTextBox == null ? null : TwoWireClkTextBox.Text, "CLK");
                case SerialProtocolType.ThreeWireSerial:
                    return ParseByteValue(ThreeWireClkTextBox == null ? null : ThreeWireClkTextBox.Text, "CLK");
                case SerialProtocolType.FourWireSerial:
                    return ParseByteValue(FourWireClkTextBox == null ? null : FourWireClkTextBox.Text, "CLK");
                default:
                    return 0x00;
            }
        }

        private byte GetEnableValue(SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.ThreeWireSerial:
                    return ParseByteValue(ThreeWireEnTextBox == null ? null : ThreeWireEnTextBox.Text, "EN");
                case SerialProtocolType.FourWireSerial:
                    return ParseByteValue(FourWireEnTextBox == null ? null : FourWireEnTextBox.Text, "EN");
                default:
                    return 0x00;
            }
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
                throw new InvalidOperationException("逻辑数据太大，请缩短文件时长或降低数据速率。");
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
                throw new InvalidOperationException("逻辑数据太大。");
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

        private static byte[] BuildSampledProtocolBytes(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            byte clockValue,
            byte enableValue,
            byte defaultByteValue,
            int samplesPerBit)
        {
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return new byte[0];
            }

            if (samplesPerBit <= 0)
            {
                throw new InvalidOperationException("每 bit 采样点数必须大于 0。");
            }

            int lineCount = GetProtocolLineCount(protocolType);
            long outputLength = checked((long)payloadBytes.Length * samplesPerBit * lineCount);
            if (outputLength > int.MaxValue)
            {
                throw new InvalidOperationException("导出 BIN 太大，请缩短文件时长或降低采样率。");
            }

            byte[] exportBytes = new byte[(int)outputLength];
            byte[][] expandedByteCache = new byte[256][];
            byte[] expandedClock = GetExpandedBytePattern(clockValue, samplesPerBit, expandedByteCache);
            byte[] expandedEnable = GetExpandedBytePattern(enableValue, samplesPerBit, expandedByteCache);
            byte[] expandedDefault = GetExpandedBytePattern(defaultByteValue, samplesPerBit, expandedByteCache);
            int outputIndex = 0;

            for (int payloadIndex = 0; payloadIndex < payloadBytes.Length; payloadIndex++)
            {
                byte[] expandedData = GetExpandedBytePattern(payloadBytes[payloadIndex], samplesPerBit, expandedByteCache);
                byte[] expandedData2 = payloadBytes2 != null && payloadIndex < payloadBytes2.Length
                    ? GetExpandedBytePattern(payloadBytes2[payloadIndex], samplesPerBit, expandedByteCache)
                    : expandedDefault;

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
                throw new InvalidOperationException("分片大小无效。");
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

            throw new InvalidOperationException(fieldName + " 必须是 0-255 或 00-FF 范围内的 byte。");
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
                    return "2 线串口";
                case SerialProtocolType.ThreeWireSerial:
                    return "3 线串口";
                case SerialProtocolType.FourWireSerial:
                    return "4 线串口";
                default:
                    return "串口";
            }
        }

        private static string SelectProtocolExportDirectory()
        {
            using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "选择协议 BIN 文件的导出目录。";
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
