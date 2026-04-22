using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using InteractiveExamples;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LCWpf
{
    public sealed class SerialWaveformBuildResult
    {
        public byte[] WaveData { get; set; }
        public byte[] ClockWaveData { get; set; }
        public byte[] ExportWaveData { get; set; }
        public SerialProtocolType ProtocolType { get; set; }
        public uint SampleRate { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public double StopBits { get; set; }
        public ParityMode Parity { get; set; }
        public int IdleBits { get; set; }
        public byte ClockValue { get; set; }
        public byte EnableValue { get; set; }
        public string InputText { get; set; }
        public long FrameCount { get; set; }
        public double DurationSeconds { get; set; }
    }

    internal sealed class SerialPreviewState
    {
        public string DataPreviewText { get; set; }
        public string DataPreviewHex { get; set; }
    }

    internal sealed class ProtocolExportChunk
    {
        public int Index { get; set; }
        public byte[] ExportBytes { get; set; }
        public string FileName { get; set; }
        public int PayloadByteCount { get; set; }
    }

    public partial class SerialPortDataProducer : UserControl
    {
        private const uint FixedSampleRate = 50000000;
        private const int ProtocolExportChunkSeconds = 5;
        private const double ProtocolExportPartitionDurationSeconds = 0.1;
        private const int ProtocolExportPartitionsPerChunk = (int)(ProtocolExportChunkSeconds / ProtocolExportPartitionDurationSeconds);
        private const int PreviewByteCount = 80;
        private const double MinimumEmptySegmentDurationMilliseconds = 5;
        private const double MinimumEmptySegmentIntervalMilliseconds = 40;
        private const int DefaultByteRepeatCount = 20;
        private const string DefaultPayloadSeedText = "0123456789";
        private int _previewSeed;

        public SerialPortDataProducer()
        {
            InitializeComponent();
            _previewSeed = Environment.TickCount;
            SampleRateTextBox.Text = FixedSampleRate.ToString(CultureInfo.InvariantCulture);
            BaudRateComboBox.Text = "115200";
            SelectComboBoxItemByText(ParityComboBox, "None");
            SelectComboBoxItemByText(DataBitsComboBox, "8");
            SelectComboBoxItemByText(StopBitsComboBox, "1");
            SampleRateTextBox.TextChanged += HandleSettingsChanged;
            DurationSecondsTextBox.TextChanged += HandleSettingsChanged;
            ByteRepeatCountTextBox.TextChanged += HandleSettingsChanged;
            PayloadSeedTextBox.TextChanged += HandleSettingsChanged;
            EmptyDataRatioTextBox.TextChanged += HandleSettingsChanged;
            DefaultByteValueTextBox.TextChanged += HandleSettingsChanged;
            ProtocolTypeComboBox.SelectionChanged += ProtocolTypeComboBox_SelectionChanged;
            BaudRateComboBox.SelectionChanged += HandleSettingsChanged;
            BaudRateComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(HandleSettingsChanged));
            ParityComboBox.SelectionChanged += HandleSettingsChanged;
            DataBitsComboBox.SelectionChanged += HandleSettingsChanged;
            StopBitsComboBox.SelectionChanged += HandleSettingsChanged;
            TwoWireClkTextBox.TextChanged += HandleSettingsChanged;
            ThreeWireClkTextBox.TextChanged += HandleSettingsChanged;
            ThreeWireEnTextBox.TextChanged += HandleSettingsChanged;
            FourWireClkTextBox.TextChanged += HandleSettingsChanged;
            FourWireEnTextBox.TextChanged += HandleSettingsChanged;
            ApplyProtocolVisibility(GetSelectedProtocolType());
            UpdatePreview(null, EventArgs.Empty);
        }

        private void HandleSettingsChanged(object sender, EventArgs e)
        {
            unchecked
            {
                _previewSeed = (_previewSeed * 397) ^ Environment.TickCount;
            }

            UpdatePreview(sender, e);
        }

        private void UpdatePreview(object sender, EventArgs e)
        {
            if (SampleRateTextBox == null
                || DurationSecondsTextBox == null
                || ByteRepeatCountTextBox == null
                || PayloadSeedTextBox == null
                || EmptyDataRatioTextBox == null
                || DefaultByteValueTextBox == null
                || GeneratedDataPreviewTextBox == null
                || HexPreviewTextBox == null)
            {
                return;
            }

            try
            {
                int previewByteCount = (int)Math.Min(GetGeneratedByteCount(), PreviewByteCount);
                SerialPreviewState previewState = BuildPreviewState(
                    GetPrimaryPayloadSeedBytes(),
                    previewByteCount,
                    GetEmptyDataRatio(),
                    GetDefaultByteValue(),
                    _previewSeed,
                    GetSampleRate());
                GeneratedDataPreviewTextBox.Text = previewState.DataPreviewText;
                HexPreviewTextBox.Text = previewState.DataPreviewHex;
            }
            catch
            {
                GeneratedDataPreviewTextBox.Text = string.Empty;
                HexPreviewTextBox.Text = string.Empty;
            }
        }

        public bool TryBuildWaveform(out SerialWaveformBuildResult buildResult, out string errorMessage)
        {
            byte[] generatedBytes = BuildPrimaryGeneratedPayloadBytes();
            byte[] generatedBytes2 = BuildSecondaryGeneratedPayloadBytes(generatedBytes.Length);
            byte[] protocolExportBytes = BuildProtocolExportBytes(
                generatedBytes,
                generatedBytes2,
                GetSelectedProtocolType(),
                GetClockValue(),
                GetEnableValue(),
                GetDefaultByteValue());

            buildResult = new SerialWaveformBuildResult
            {
                WaveData = generatedBytes,
                ClockWaveData = new byte[0],
                ExportWaveData = protocolExportBytes,
                ProtocolType = GetSelectedProtocolType(),
                SampleRate = GetSampleRate(),
                BaudRate = GetBaudRate(),
                DataBits = GetDataBits(),
                StopBits = GetStopBits(),
                Parity = GetParityMode(),
                IdleBits = 0,
                ClockValue = GetClockValue(),
                EnableValue = GetEnableValue(),
                InputText = PayloadSeedTextBox == null ? string.Empty : PayloadSeedTextBox.Text ?? string.Empty,
                FrameCount = GetProtocolFrameCount(generatedBytes.Length, generatedBytes2.Length, GetSelectedProtocolType()),
                DurationSeconds = GetDurationSeconds()
            };
            errorMessage = null;
            return true;
        }

        private void ProtocolTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyProtocolVisibility(GetSelectedProtocolType());
            HandleSettingsChanged(sender, e);
        }

        private void ExportTestDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int emptyDataRatio = GetEmptyDataRatio();
                byte defaultByteValue = GetDefaultByteValue();
                byte[] generatedBytes = BuildPrimaryGeneratedPayloadBytes();
                int defaultByteCount = CountByteOccurrences(generatedBytes, defaultByteValue);

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
                    DefaultExt = ".bin",
                    FileName = "test_data.bin"
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                File.WriteAllBytes(saveDialog.FileName, generatedBytes);
                MessageBox.Show(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "BIN file generated.{0}{0}Path: {1}{0}Bytes: {2:N0}{0}Default value: 0x{3:X2}{0}Default-byte count: {4:N0}{0}Configured empty ratio: {5}%",
                        Environment.NewLine,
                        saveDialog.FileName,
                        generatedBytes.Length,
                        defaultByteValue,
                        defaultByteCount,
                        emptyDataRatio),
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Generation failed:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportProtocolDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte defaultByteValue = GetDefaultByteValue();
                byte[] payloadBytes = BuildPrimaryGeneratedPayloadBytes();
                byte[] payloadBytes2 = BuildSecondaryGeneratedPayloadBytes(payloadBytes.Length);
                SerialProtocolType protocolType = GetSelectedProtocolType();
                long frameCount = GetProtocolFrameCount(payloadBytes.Length, payloadBytes2.Length, protocolType);

                if (protocolType == SerialProtocolType.TwoWireSerial
                    || protocolType == SerialProtocolType.ThreeWireSerial
                    || protocolType == SerialProtocolType.FourWireSerial)
                {
                    string exportDirectory = SelectProtocolExportDirectory();
                    if (string.IsNullOrWhiteSpace(exportDirectory))
                    {
                        return;
                    }

                    DateTime exportTimestamp = DateTime.Now;
                    ProtocolExportChunk[] chunks = BuildProtocolExportChunks(
                        payloadBytes,
                        payloadBytes2,
                        protocolType,
                        GetClockValue(),
                        GetEnableValue(),
                        defaultByteValue,
                        GetSampleRate(),
                        GetDurationSeconds(),
                        exportTimestamp);

                    if (chunks.Length == 0)
                    {
                        throw new InvalidOperationException("No protocol data was generated for export.");
                    }

                    for (int i = 0; i < chunks.Length; i++)
                    {
                        string outputPath = Path.Combine(exportDirectory, chunks[i].FileName);
                        File.WriteAllBytes(outputPath, chunks[i].ExportBytes);
                    }

                    MessageBox.Show(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Protocol BIN files generated.{0}{0}Protocol: {1}{0}Directory: {2}{0}Files: {3:N0}{0}Payload bytes: {4:N0}{0}Frames: {5:N0}",
                            Environment.NewLine,
                            GetProtocolDisplayName(protocolType),
                            exportDirectory,
                            chunks.Length,
                            protocolType == SerialProtocolType.FourWireSerial ? Math.Max(payloadBytes.Length, payloadBytes2.Length) : payloadBytes.Length,
                            frameCount),
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                byte[] protocolBytes = BuildProtocolExportBytes(
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    GetClockValue(),
                    GetEnableValue(),
                    defaultByteValue);

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
                    DefaultExt = ".bin",
                    FileName = BuildProtocolExportFileName(protocolType)
                };

                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                File.WriteAllBytes(saveDialog.FileName, protocolBytes);
                MessageBox.Show(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Protocol BIN file generated.{0}{0}Protocol: {1}{0}Path: {2}{0}Payload bytes: {3:N0}{0}Export bytes: {4:N0}{0}Frames: {5:N0}",
                        Environment.NewLine,
                        GetProtocolDisplayName(protocolType),
                        saveDialog.FileName,
                        payloadBytes.Length,
                        protocolType == SerialProtocolType.FourWireSerial ? Math.Max(payloadBytes.Length, payloadBytes2.Length) : payloadBytes.Length,
                        protocolBytes.Length,
                        frameCount),
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Protocol export failed:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static SerialPreviewState BuildPreviewState(byte[] payloadSeedBytes, int previewByteCount, int emptyDataRatio, byte defaultByteValue, int randomSeed, uint sampleRate)
        {
            byte[] previewBytes = BuildGeneratedBytes(payloadSeedBytes, previewByteCount, emptyDataRatio, defaultByteValue, randomSeed, sampleRate);
            return new SerialPreviewState
            {
                DataPreviewText = BuildAsciiPreview(previewBytes),
                DataPreviewHex = ConvertBytesToHex(previewBytes)
            };
        }

        private static byte[] BuildGeneratedBytes(byte[] payloadSeedBytes, int byteCount, int emptyDataRatio, byte defaultByteValue, int randomSeed, uint sampleRate)
        {
            if (byteCount <= 0)
            {
                return new byte[0];
            }

            byte[] normalizedPayloadSeedBytes =
                payloadSeedBytes == null || payloadSeedBytes.Length == 0
                    ? Encoding.ASCII.GetBytes(DefaultPayloadSeedText)
                    : payloadSeedBytes;
            byte[] generatedBytes = new byte[byteCount];
            bool[] defaultMask = BuildSegmentedDefaultMask(byteCount, emptyDataRatio, randomSeed, sampleRate);
            int payloadIndex = 0;

            for (int i = 0; i < generatedBytes.Length; i++)
            {
                if (defaultMask[i])
                {
                    generatedBytes[i] = defaultByteValue;
                    continue;
                }

                generatedBytes[i] = normalizedPayloadSeedBytes[payloadIndex % normalizedPayloadSeedBytes.Length];
                payloadIndex++;
            }

            return generatedBytes;
        }

        private static bool[] BuildSegmentedDefaultMask(int byteCount, int emptyDataRatio, int randomSeed, uint sampleRate)
        {
            bool[] defaultMask = new bool[byteCount];
            if (byteCount <= 0 || emptyDataRatio <= 0)
            {
                return defaultMask;
            }

            int defaultByteCount = (int)Math.Round(
                (((long)byteCount) * emptyDataRatio) / 100.0,
                MidpointRounding.AwayFromZero);
            defaultByteCount = Math.Max(0, Math.Min(byteCount, defaultByteCount));
            if (defaultByteCount == 0)
            {
                return defaultMask;
            }

            if (defaultByteCount >= byteCount)
            {
                for (int i = 0; i < defaultMask.Length; i++)
                {
                    defaultMask[i] = true;
                }

                return defaultMask;
            }

            int emptySegmentMinBytes = GetByteCountForDuration(sampleRate, MinimumEmptySegmentDurationMilliseconds, byteCount);
            int emptySegmentMinGapBytes = GetByteCountForDuration(sampleRate, MinimumEmptySegmentIntervalMilliseconds, byteCount);
            int nonDefaultByteCount = byteCount - defaultByteCount;
            Random random = new Random(randomSeed);
            int emptySegmentCount = DetermineEmptySegmentCount(
                defaultByteCount,
                nonDefaultByteCount,
                emptySegmentMinBytes,
                emptySegmentMinGapBytes);
            int[] emptySegmentLengths = BuildEmptySegmentLengths(
                defaultByteCount,
                emptySegmentCount,
                emptySegmentMinBytes,
                random);
            int[] gapLengths = BuildGapLengths(
                nonDefaultByteCount,
                emptySegmentCount,
                emptySegmentMinGapBytes,
                random);
            int index = gapLengths[0];

            for (int i = 0; i < emptySegmentLengths.Length; i++)
            {
                int segmentLength = emptySegmentLengths[i];
                for (int j = 0; j < segmentLength && index + j < defaultMask.Length; j++)
                {
                    defaultMask[index + j] = true;
                }

                index += segmentLength + gapLengths[i + 1];
            }

            return defaultMask;
        }

        private static int DetermineEmptySegmentCount(int defaultByteCount, int nonDefaultByteCount, int emptySegmentMinBytes, int emptySegmentMinGapBytes)
        {
            if (defaultByteCount <= 0)
            {
                return 0;
            }

            int maximumSegmentsByLength = Math.Max(1, defaultByteCount / Math.Max(1, emptySegmentMinBytes));
            int maximumSegmentsByGap = nonDefaultByteCount <= 0
                ? 1
                : Math.Max(1, (nonDefaultByteCount / Math.Max(1, emptySegmentMinGapBytes)) + 1);
            return Math.Max(1, Math.Min(maximumSegmentsByLength, maximumSegmentsByGap));
        }

        private static int[] BuildEmptySegmentLengths(int defaultByteCount, int emptySegmentCount, int emptySegmentMinBytes, Random random)
        {
            if (emptySegmentCount <= 1)
            {
                return new[] { defaultByteCount };
            }

            int[] lengths = new int[emptySegmentCount];
            int assignedBytes = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = emptySegmentMinBytes;
                assignedBytes += emptySegmentMinBytes;
            }

            int remainingBytes = Math.Max(0, defaultByteCount - assignedBytes);
            DistributeBytes(lengths, remainingBytes, int.MaxValue, random);

            ShuffleArray(lengths, random);
            return lengths;
        }

        private static int[] BuildGapLengths(int nonDefaultByteCount, int emptySegmentCount, int emptySegmentMinGapBytes, Random random)
        {
            int[] gapLengths = new int[Math.Max(1, emptySegmentCount + 1)];
            if (emptySegmentCount <= 0)
            {
                gapLengths[0] = nonDefaultByteCount;
                return gapLengths;
            }

            int internalGapCount = Math.Max(0, emptySegmentCount - 1);
            int requiredGapBytes = internalGapCount * emptySegmentMinGapBytes;
            for (int i = 1; i < emptySegmentCount; i++)
            {
                gapLengths[i] = emptySegmentMinGapBytes;
            }

            int remainingBytes = Math.Max(0, nonDefaultByteCount - requiredGapBytes);
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

        private static int GetByteCountForDuration(uint sampleRate, double durationMilliseconds, int byteCountLimit)
        {
            if (byteCountLimit <= 0)
            {
                return 1;
            }

            long byteCount = GetGeneratedByteCount(sampleRate, durationMilliseconds / 1000.0);
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

        private static string BuildAsciiPreview(byte[] previewBytes)
        {
            if (previewBytes == null || previewBytes.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(previewBytes.Length);
            for (int i = 0; i < previewBytes.Length; i++)
            {
                byte value = previewBytes[i];
                builder.Append(value >= 0x20 && value <= 0x7E ? (char)value : '.');
            }

            return builder.ToString();
        }

        private static string ConvertBytesToHex(byte[] bytes)
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
                    builder.Append(' ');
                }

                builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static int CountByteOccurrences(byte[] bytes, byte targetValue)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == targetValue)
                {
                    count++;
                }
            }

            return count;
        }

        private long GetGeneratedByteCount()
        {
            long byteCount = GetGeneratedByteCount(GetSampleRate(), GetDurationSeconds());
            if (byteCount <= 0)
            {
                return 1;
            }

            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("Generated data is too large.");
            }

            return byteCount;
        }

        private static long GetGeneratedByteCount(uint sampleRate, double durationSeconds)
        {
            return (long)Math.Round((sampleRate * durationSeconds) / 8.0, MidpointRounding.AwayFromZero);
        }

        private byte[] BuildPrimaryGeneratedPayloadBytes()
        {
            return BuildGeneratedBytes(
                GetPrimaryPayloadSeedBytes(),
                (int)GetGeneratedByteCount(),
                GetEmptyDataRatio(),
                GetDefaultByteValue(),
                _previewSeed,
                GetSampleRate());
        }

        private byte[] BuildSecondaryGeneratedPayloadBytes(int byteCount)
        {
            if (GetSelectedProtocolType() != SerialProtocolType.FourWireSerial)
            {
                return new byte[0];
            }

            return BuildGeneratedBytes(
                GetPrimaryPayloadSeedBytes(),
                byteCount,
                GetEmptyDataRatio(),
                GetDefaultByteValue(),
                _previewSeed ^ 0x5A5A5A5A,
                GetSampleRate());
        }

        private static byte[] BuildProtocolExportBytes(byte[] payloadBytes, byte[] payloadBytes2, SerialProtocolType protocolType, byte clockValue, byte enableValue, byte defaultByteValue)
        {
            if ((payloadBytes == null || payloadBytes.Length == 0)
                && (payloadBytes2 == null || payloadBytes2.Length == 0))
            {
                return new byte[0];
            }

            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return BuildTwoWireExportBytes(payloadBytes, clockValue);
                case SerialProtocolType.ThreeWireSerial:
                    return BuildThreeWireExportBytes(payloadBytes, clockValue, enableValue);
                case SerialProtocolType.FourWireSerial:
                    return BuildFourWireExportBytes(payloadBytes, payloadBytes2, clockValue, enableValue, defaultByteValue);
                default:
                    return (byte[])payloadBytes.Clone();
            }
        }

        private static byte[] BuildTwoWireExportBytes(byte[] payloadBytes, byte clockValue)
        {
            byte[] exportBytes = new byte[payloadBytes.Length * 2];
            int outputIndex = 0;
            for (int i = 0; i < payloadBytes.Length; i++)
            {
                exportBytes[outputIndex++] = clockValue;
                exportBytes[outputIndex++] = payloadBytes[i];
            }

            return exportBytes;
        }

        private static byte[] BuildThreeWireExportBytes(byte[] payloadBytes, byte clockValue, byte enableValue)
        {
            byte[] exportBytes = new byte[payloadBytes.Length * 3];
            int outputIndex = 0;
            for (int i = 0; i < payloadBytes.Length; i++)
            {
                exportBytes[outputIndex++] = clockValue;
                exportBytes[outputIndex++] = enableValue;
                exportBytes[outputIndex++] = payloadBytes[i];
            }

            return exportBytes;
        }

        private static byte[] BuildFourWireExportBytes(byte[] payloadBytes, byte[] payloadBytes2, byte clockValue, byte enableValue, byte defaultByteValue)
        {
            int payloadLength1 = payloadBytes == null ? 0 : payloadBytes.Length;
            int payloadLength2 = payloadBytes2 == null ? 0 : payloadBytes2.Length;
            int frameCount = Math.Max(payloadLength1, payloadLength2);
            byte[] exportBytes = new byte[frameCount * 4];
            int outputIndex = 0;

            for (int i = 0; i < frameCount; i++)
            {
                exportBytes[outputIndex++] = clockValue;
                exportBytes[outputIndex++] = enableValue;
                exportBytes[outputIndex++] = i < payloadLength1 ? payloadBytes[i] : defaultByteValue;
                exportBytes[outputIndex++] = i < payloadLength2 ? payloadBytes2[i] : defaultByteValue;
            }

            return exportBytes;
        }

        private static long GetProtocolFrameCount(int payloadByteCount, int payloadByteCount2, SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.FourWireSerial:
                    return Math.Max(payloadByteCount, payloadByteCount2);
                default:
                    return payloadByteCount;
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

        private static string BuildProtocolExportFileName(SerialProtocolType protocolType)
        {
            switch (protocolType)
            {
                case SerialProtocolType.TwoWireSerial:
                    return "two_wire_protocol_data.bin";
                case SerialProtocolType.ThreeWireSerial:
                    return "three_wire_protocol_data.bin";
                case SerialProtocolType.FourWireSerial:
                    return "four_wire_protocol_data.bin";
                default:
                    return "uart_protocol_data.bin";
            }
        }

        private static string SelectProtocolExportDirectory()
        {
            using (Forms.FolderBrowserDialog folderDialog = new Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select a folder to export protocol BIN files.";
                folderDialog.ShowNewFolderButton = true;
                return folderDialog.ShowDialog() == Forms.DialogResult.OK
                    ? folderDialog.SelectedPath
                    : null;
            }
        }

        private static ProtocolExportChunk[] BuildProtocolExportChunks(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            SerialProtocolType protocolType,
            byte clockValue,
            byte enableValue,
            byte defaultByteValue,
            uint sampleRate,
            double durationSeconds,
            DateTime exportTimestamp)
        {
            int totalFrameCount = Math.Max(payloadBytes == null ? 0 : payloadBytes.Length, payloadBytes2 == null ? 0 : payloadBytes2.Length);
            if (totalFrameCount <= 0)
            {
                return new ProtocolExportChunk[0];
            }

            int chunkCount = Math.Max(1, (int)Math.Ceiling(durationSeconds / ProtocolExportChunkSeconds));
            ProtocolExportChunk[] chunks = new ProtocolExportChunk[chunkCount];
            string timestampText = BuildExportTimestampText(exportTimestamp);
            int lineCount = GetProtocolLineCount(protocolType);

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                double chunkStartSeconds = chunkIndex * ProtocolExportChunkSeconds;
                double chunkEndSeconds = Math.Min(durationSeconds, (chunkIndex + 1) * ProtocolExportChunkSeconds);
                int startFrameIndex = GetFrameIndexAtTime(sampleRate, durationSeconds, totalFrameCount, chunkStartSeconds);
                int endFrameIndex = GetFrameIndexAtTime(sampleRate, durationSeconds, totalFrameCount, chunkEndSeconds);

                if (chunkIndex == chunkCount - 1)
                {
                    endFrameIndex = totalFrameCount;
                }

                endFrameIndex = Math.Max(startFrameIndex, Math.Min(totalFrameCount, endFrameIndex));
                byte[] chunkPayloadBytes = SliceChannelBytes(payloadBytes, startFrameIndex, endFrameIndex, defaultByteValue);
                byte[] chunkPayloadBytes2 = SliceChannelBytes(payloadBytes2, startFrameIndex, endFrameIndex, defaultByteValue);
                string activePartitionList = BuildActivePartitionList(
                    chunkPayloadBytes,
                    chunkPayloadBytes2,
                    defaultByteValue,
                    sampleRate,
                    durationSeconds,
                    chunkStartSeconds,
                    chunkEndSeconds,
                    totalFrameCount);

                chunks[chunkIndex] = new ProtocolExportChunk
                {
                    Index = chunkIndex + 1,
                    ExportBytes = BuildProtocolExportBytes(chunkPayloadBytes, chunkPayloadBytes2, protocolType, clockValue, enableValue, defaultByteValue),
                    FileName = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0};{1};{2};{3}-{4}.bin",
                        lineCount,
                        sampleRate,
                        timestampText,
                        activePartitionList,
                        chunkIndex + 1),
                    PayloadByteCount = Math.Max(chunkPayloadBytes.Length, chunkPayloadBytes2.Length)
                };
            }

            return chunks;
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

        private static string BuildExportTimestampText(DateTime exportTimestamp)
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

        private static int GetFrameIndexAtTime(uint sampleRate, double totalDurationSeconds, int totalFrameCount, double timeSeconds)
        {
            if (totalFrameCount <= 0 || totalDurationSeconds <= 0)
            {
                return 0;
            }

            if (timeSeconds <= 0)
            {
                return 0;
            }

            if (timeSeconds >= totalDurationSeconds)
            {
                return totalFrameCount;
            }

            long boundaryBySampleRate = GetGeneratedByteCount(sampleRate, timeSeconds);
            if (boundaryBySampleRate >= 0 && boundaryBySampleRate <= totalFrameCount)
            {
                return (int)boundaryBySampleRate;
            }

            return (int)Math.Round((totalFrameCount * timeSeconds) / totalDurationSeconds, MidpointRounding.AwayFromZero);
        }

        private static byte[] SliceChannelBytes(byte[] sourceBytes, int startFrameIndex, int endFrameIndex, byte defaultByteValue)
        {
            int length = Math.Max(0, endFrameIndex - startFrameIndex);
            byte[] slice = new byte[length];
            if (length == 0)
            {
                return slice;
            }

            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                for (int i = 0; i < slice.Length; i++)
                {
                    slice[i] = defaultByteValue;
                }

                return slice;
            }

            for (int i = 0; i < length; i++)
            {
                int sourceIndex = startFrameIndex + i;
                slice[i] = sourceIndex < sourceBytes.Length ? sourceBytes[sourceIndex] : defaultByteValue;
            }

            return slice;
        }

        private static string BuildActivePartitionList(
            byte[] payloadBytes,
            byte[] payloadBytes2,
            byte defaultByteValue,
            uint sampleRate,
            double totalDurationSeconds,
            double chunkStartSeconds,
            double chunkEndSeconds,
            int totalFrameCount)
        {
            int partitionCount = GetPartitionCountForChunk(chunkStartSeconds, chunkEndSeconds);
            StringBuilder builder = new StringBuilder();
            int chunkStartFrame = GetFrameIndexAtTime(sampleRate, totalDurationSeconds, totalFrameCount, chunkStartSeconds);

            for (int partitionIndex = 0; partitionIndex < partitionCount; partitionIndex++)
            {
                double partitionStartTime = chunkStartSeconds + (partitionIndex * ProtocolExportPartitionDurationSeconds);
                double partitionEndTime = Math.Min(chunkEndSeconds, partitionStartTime + ProtocolExportPartitionDurationSeconds);
                int partitionStartFrame = GetFrameIndexAtTime(sampleRate, totalDurationSeconds, totalFrameCount, partitionStartTime);
                int partitionEndFrame = GetFrameIndexAtTime(sampleRate, totalDurationSeconds, totalFrameCount, partitionEndTime);
                int localStart = Math.Max(0, partitionStartFrame - chunkStartFrame);
                int localEnd = Math.Max(localStart, partitionEndFrame - chunkStartFrame);

                if (ContainsNonDefaultData(payloadBytes, payloadBytes2, localStart, localEnd, defaultByteValue) == false)
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

        private static int GetPartitionCountForChunk(double chunkStartSeconds, double chunkEndSeconds)
        {
            double chunkDuration = Math.Max(0, chunkEndSeconds - chunkStartSeconds);
            int partitionCount = (int)Math.Ceiling(chunkDuration / ProtocolExportPartitionDurationSeconds);
            return Math.Max(1, Math.Min(ProtocolExportPartitionsPerChunk, partitionCount));
        }

        private static bool ContainsNonDefaultData(byte[] payloadBytes, byte[] payloadBytes2, int startIndex, int endIndex, byte defaultByteValue)
        {
            for (int i = startIndex; i < endIndex; i++)
            {
                if (payloadBytes != null && i < payloadBytes.Length && payloadBytes[i] != defaultByteValue)
                {
                    return true;
                }

                if (payloadBytes2 != null && i < payloadBytes2.Length && payloadBytes2[i] != defaultByteValue)
                {
                    return true;
                }
            }

            return false;
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

        private uint GetSampleRate()
        {
            uint value;
            if (uint.TryParse(SampleRateTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            return FixedSampleRate;
        }

        private int GetBaudRate()
        {
            string text = BaudRateComboBox == null ? null : BaudRateComboBox.Text;
            int value;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            return 115200;
        }

        private int GetDataBits()
        {
            return GetSelectedInteger(DataBitsComboBox, 8);
        }

        private double GetStopBits()
        {
            return GetSelectedDouble(StopBitsComboBox, 1);
        }

        private ParityMode GetParityMode()
        {
            string text = GetComboBoxText(ParityComboBox);
            switch (text)
            {
                case "Odd":
                    return ParityMode.Odd;
                case "Even":
                    return ParityMode.Even;
                case "Mark":
                    return ParityMode.Mark;
                case "Space":
                    return ParityMode.Space;
                default:
                    return ParityMode.None;
            }
        }

        private double GetDurationSeconds()
        {
            double value;
            if (double.TryParse(DurationSecondsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            if (double.TryParse(DurationSecondsTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0)
            {
                return value;
            }

            throw new InvalidOperationException("Duration must be a positive number.");
        }

        private int GetEmptyDataRatio()
        {
            int value;
            if (int.TryParse(EmptyDataRatioTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == false &&
                int.TryParse(EmptyDataRatioTextBox.Text, out value) == false)
            {
                throw new InvalidOperationException("Empty data ratio must be an integer.");
            }

            if (value < 0 || value > 100)
            {
                throw new InvalidOperationException("Empty data ratio must be between 0 and 100.");
            }

            return value;
        }

        private byte GetDefaultByteValue()
        {
            string text = DefaultByteValueTextBox == null ? null : DefaultByteValueTextBox.Text;
            string normalizedText = string.IsNullOrWhiteSpace(text) ? "00" : text.Trim();
            if (normalizedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalizedText = normalizedText.Substring(2);
            }

            byte value;
            if (byte.TryParse(normalizedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            throw new InvalidOperationException("Default value must be a hex byte like 00 or FF.");
        }

        private byte GetClockValue()
        {
            switch (GetSelectedProtocolType())
            {
                case SerialProtocolType.TwoWireSerial:
                    return ParseHexByte(TwoWireClkTextBox == null ? null : TwoWireClkTextBox.Text, "CLK");
                case SerialProtocolType.ThreeWireSerial:
                    return ParseHexByte(ThreeWireClkTextBox == null ? null : ThreeWireClkTextBox.Text, "CLK");
                case SerialProtocolType.FourWireSerial:
                    return ParseHexByte(FourWireClkTextBox == null ? null : FourWireClkTextBox.Text, "CLK");
                default:
                    return 0x00;
            }
        }

        private byte GetEnableValue()
        {
            switch (GetSelectedProtocolType())
            {
                case SerialProtocolType.ThreeWireSerial:
                    return ParseHexByte(ThreeWireEnTextBox == null ? null : ThreeWireEnTextBox.Text, "EN");
                case SerialProtocolType.FourWireSerial:
                    return ParseHexByte(FourWireEnTextBox == null ? null : FourWireEnTextBox.Text, "EN");
                default:
                    return 0x00;
            }
        }

        private byte[] GetPrimaryPayloadSeedBytes()
        {
            string text = PayloadSeedTextBox == null ? null : PayloadSeedTextBox.Text;
            string normalizedText = string.IsNullOrEmpty(text) ? DefaultPayloadSeedText : text;
            byte[] bytes = Encoding.ASCII.GetBytes(normalizedText);
            if (bytes.Length == 0)
            {
                bytes = Encoding.ASCII.GetBytes(DefaultPayloadSeedText);
            }

            return ExpandBytesByRepeatCount(bytes, GetByteRepeatCount());
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

        private static int GetSelectedInteger(ComboBox comboBox, int fallbackValue)
        {
            int value;
            if (int.TryParse(GetComboBoxText(comboBox), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            return fallbackValue;
        }

        private static double GetSelectedDouble(ComboBox comboBox, double fallbackValue)
        {
            double value;
            string text = GetComboBoxText(comboBox);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            return fallbackValue;
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

                string itemText = Convert.ToString(item.Content, CultureInfo.InvariantCulture);
                if (string.Equals(itemText, expectedText, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.Text = expectedText;
        }

        private int GetByteRepeatCount()
        {
            string text = ByteRepeatCountTextBox == null ? null : ByteRepeatCountTextBox.Text;
            string normalizedText = string.IsNullOrWhiteSpace(text)
                ? DefaultByteRepeatCount.ToString(CultureInfo.InvariantCulture)
                : text.Trim();
            int value;
            if (int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) == false &&
                int.TryParse(normalizedText, out value) == false)
            {
                throw new InvalidOperationException("Byte repeat count must be a positive integer.");
            }

            if (value <= 0)
            {
                throw new InvalidOperationException("Byte repeat count must be greater than zero.");
            }

            return value;
        }

        private static byte[] ExpandBytesByRepeatCount(byte[] sourceBytes, int repeatCount)
        {
            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                return new byte[0];
            }

            if (repeatCount <= 1)
            {
                return (byte[])sourceBytes.Clone();
            }

            int expandedLength;
            try
            {
                expandedLength = checked(sourceBytes.Length * repeatCount);
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException("Expanded payload is too large.", ex);
            }

            byte[] expandedBytes = new byte[expandedLength];
            int outputIndex = 0;

            for (int i = 0; i < sourceBytes.Length; i++)
            {
                byte value = sourceBytes[i];
                for (int j = 0; j < repeatCount; j++)
                {
                    expandedBytes[outputIndex++] = value;
                }
            }

            return expandedBytes;
        }

        private static byte ParseHexByte(string text, string fieldName)
        {
            string normalizedText = string.IsNullOrWhiteSpace(text) ? "00" : text.Trim();
            if (normalizedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalizedText = normalizedText.Substring(2);
            }

            byte value;
            if (byte.TryParse(normalizedText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            throw new InvalidOperationException(fieldName + " must be a hex byte like 00 or FF.");
        }
    }

    public enum ParityMode
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }
}
