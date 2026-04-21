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
        public uint SampleRate { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public double StopBits { get; set; }
        public ParityMode Parity { get; set; }
        public int IdleBits { get; set; }
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
            SampleRateTextBox.TextChanged += HandleSettingsChanged;
            DurationSecondsTextBox.TextChanged += HandleSettingsChanged;
            PayloadSeedTextBox.TextChanged += HandleSettingsChanged;
            EmptyDataRatioTextBox.TextChanged += HandleSettingsChanged;
            DefaultByteValueTextBox.TextChanged += HandleSettingsChanged;
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
            try
            {
                int previewByteCount = (int)Math.Min(GetGeneratedByteCount(), PreviewByteCount);
                SerialPreviewState previewState = BuildPreviewState(
                    GetPayloadSeedBytes(),
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
            buildResult = new SerialWaveformBuildResult
            {
                WaveData = new byte[0],
                ClockWaveData = new byte[0],
                ExportWaveData = new byte[0],
                SampleRate = 0,
                BaudRate = 0,
                DataBits = 0,
                StopBits = 0,
                Parity = ParityMode.None,
                IdleBits = 0,
                InputText = string.Empty,
                FrameCount = 0,
                DurationSeconds = 0
            };
            errorMessage = null;
            return true;
        }

        private void ExportTestDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int emptyDataRatio = GetEmptyDataRatio();
                byte defaultByteValue = GetDefaultByteValue();
                byte[] generatedBytes = BuildGeneratedBytes(
                    GetPayloadSeedBytes(),
                    (int)GetGeneratedByteCount(),
                    emptyDataRatio,
                    defaultByteValue,
                    _previewSeed);
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

        private uint GetSampleRate()
        {
            uint value;
            if (uint.TryParse(SampleRateTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return value;
            }

            return FixedSampleRate;
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

        private byte[] GetPayloadSeedBytes()
        {
            string text = PayloadSeedTextBox == null ? null : PayloadSeedTextBox.Text;
            string normalizedText = string.IsNullOrEmpty(text) ? DefaultPayloadSeedText : text;
            byte[] bytes = Encoding.ASCII.GetBytes(normalizedText);
            return bytes.Length == 0 ? Encoding.ASCII.GetBytes(DefaultPayloadSeedText) : bytes;
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
