using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

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

    public partial class SerialPortDataProducer : UserControl
    {
        private const uint FixedSampleRate = 50000000;
        private const int PreviewByteCount = 80;
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
                    _previewSeed);
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
                byte[] protocolBytes = BuildProtocolExportBytes(
                    payloadBytes,
                    payloadBytes2,
                    protocolType,
                    GetClockValue(),
                    GetEnableValue(),
                    defaultByteValue);
                long frameCount = GetProtocolFrameCount(payloadBytes.Length, payloadBytes2.Length, protocolType);

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

        private static SerialPreviewState BuildPreviewState(byte[] payloadSeedBytes, int previewByteCount, int emptyDataRatio, byte defaultByteValue, int randomSeed)
        {
            byte[] previewBytes = BuildGeneratedBytes(payloadSeedBytes, previewByteCount, emptyDataRatio, defaultByteValue, randomSeed);
            return new SerialPreviewState
            {
                DataPreviewText = BuildAsciiPreview(previewBytes),
                DataPreviewHex = ConvertBytesToHex(previewBytes)
            };
        }

        private static byte[] BuildGeneratedBytes(byte[] payloadSeedBytes, int byteCount, int emptyDataRatio, byte defaultByteValue, int randomSeed)
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
            bool[] defaultMask = BuildRandomDefaultMask(byteCount, emptyDataRatio, randomSeed);
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

        private static bool[] BuildRandomDefaultMask(int byteCount, int emptyDataRatio, int randomSeed)
        {
            bool[] defaultMask = new bool[byteCount];
            if (byteCount <= 0 || emptyDataRatio <= 0)
            {
                return defaultMask;
            }

            int defaultByteCount = (int)Math.Round((byteCount * emptyDataRatio) / 100.0, MidpointRounding.AwayFromZero);
            defaultByteCount = Math.Max(0, Math.Min(byteCount, defaultByteCount));
            if (defaultByteCount == 0)
            {
                return defaultMask;
            }

            Random random = new Random(randomSeed);
            int maxClusterLength = Math.Max(8, byteCount / 8);
            int remainingDefaultBytes = defaultByteCount;
            int attempts = 0;
            int maxAttempts = Math.Max(byteCount * 6, 64);

            while (remainingDefaultBytes > 0 && attempts < maxAttempts)
            {
                int startIndex = random.Next(byteCount);
                int clusterLength = 1 + (int)(Math.Pow(random.NextDouble(), 0.35) * maxClusterLength);
                if (clusterLength <= 0)
                {
                    clusterLength = 1;
                }

                for (int i = 0; i < clusterLength && remainingDefaultBytes > 0; i++)
                {
                    int index = startIndex + i;
                    if (index >= byteCount)
                    {
                        break;
                    }

                    if (defaultMask[index])
                    {
                        continue;
                    }

                    defaultMask[index] = true;
                    remainingDefaultBytes--;
                }

                attempts++;
            }

            while (remainingDefaultBytes > 0)
            {
                int index = random.Next(byteCount);
                if (defaultMask[index])
                {
                    continue;
                }

                defaultMask[index] = true;
                remainingDefaultBytes--;
            }

            return defaultMask;
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
            uint sampleRate = GetSampleRate();
            double durationSeconds = GetDurationSeconds();
            long byteCount = (long)Math.Round((sampleRate * durationSeconds) / 8.0, MidpointRounding.AwayFromZero);
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

        private byte[] BuildPrimaryGeneratedPayloadBytes()
        {
            return BuildGeneratedBytes(
                GetPrimaryPayloadSeedBytes(),
                (int)GetGeneratedByteCount(),
                GetEmptyDataRatio(),
                GetDefaultByteValue(),
                _previewSeed);
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
                _previewSeed ^ 0x5A5A5A5A);
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
            return bytes.Length == 0 ? Encoding.ASCII.GetBytes(DefaultPayloadSeedText) : bytes;
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

    public enum SerialProtocolType
    {
        Uart,
        TwoWireSerial,
        ThreeWireSerial,
        FourWireSerial
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
