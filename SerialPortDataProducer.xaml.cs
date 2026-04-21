using System;
using System.Collections.Generic;
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
        public long FrameCount { get; set; }
        public int SamplesPerBit { get; set; }
        public int SamplesPerFrame { get; set; }
        public long TotalSamples { get; set; }
        public long DataWaveByteCount { get; set; }
        public long ExportByteCount { get; set; }
        public string DataPreviewText { get; set; }
        public string DataPreviewHex { get; set; }
        public string FramePreviewText { get; set; }
        public string ProtocolPreviewText { get; set; }
    }

    internal sealed class TwoWireWaveformBuffers
    {
        public byte[] DataWaveBytes { get; set; }
        public byte[] ClockWaveBytes { get; set; }
        public byte[] ExportWaveBytes { get; set; }
    }

    public partial class SerialPortDataProducer : UserControl
    {
        private const uint FixedSampleRate = 50000000;
        private const int PreviewCharacterCount = 80;
        private const int PreviewFrameCount = 6;
        private const int PreviewProtocolByteCount = 32;
        private const string DefaultPayloadSeedText = "0123456789";

        private const uint ProtocolFrameHeader = 0x499602D2;
        private const uint ProtocolFrameTail = 0xB669FD2E;
        private const ushort ProtocolDataLength = 1020;
        private const ushort ProtocolDataType = 0x0A0A;
        private const ushort ProtocolInterfaceAddress = 0x0101;
        private const int ProtocolPayloadLength = ProtocolDataLength - 2 - 2;
        private const int ProtocolFrameByteCount = 4 + 2 + 2 + 2 + ProtocolPayloadLength + 2 + 4;

        public SerialPortDataProducer()
        {
            InitializeComponent();

            BaudRateComboBox.SelectionChanged += UpdatePreview;
            DataBitsComboBox.SelectionChanged += UpdatePreview;
            StopBitsComboBox.SelectionChanged += UpdatePreview;
            ParityComboBox.SelectionChanged += UpdatePreview;
            IdleBitsTextBox.TextChanged += UpdatePreview;
            SampleRateTextBox.TextChanged += UpdatePreview;
            DurationSecondsTextBox.TextChanged += UpdatePreview;
            PayloadSeedTextBox.TextChanged += UpdatePreview;

            ClockModeTextBlock.Text = "Generates a UART data waveform from fixed protocol frames and keeps an auxiliary clock waveform for export preview.";
            UpdatePreview(null, EventArgs.Empty);
        }

        private void UpdatePreview(object sender, EventArgs e)
        {
            try
            {
                uint baudRate = GetBaudRate();
                int dataBits = GetDataBits();
                double stopBits = GetStopBitsValue();
                ParityMode parity = GetParityMode();
                int idleBits = GetIdleBits();
                uint sampleRate = GetSampleRate();
                double durationSeconds = GetDurationSeconds();

                EnsureSupportedProtocolConfiguration(dataBits);

                SerialPreviewState previewState = BuildPreviewState(baudRate, dataBits, stopBits, parity, idleBits, sampleRate, durationSeconds);
                GeneratedDataPreviewTextBox.Text = previewState.DataPreviewText;
                HexPreviewTextBox.Text = previewState.DataPreviewHex;
                FramePreviewTextBox.Text = previewState.FramePreviewText;
                ProtocolPreviewTextBox.Text = previewState.ProtocolPreviewText;

                InfoTextBlock.Text =
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Baud rate: {0} bps{12}Data bits: {1}{12}Stop bits: {2}{12}Parity: {3}{12}Idle bits: {4}{12}Sample rate: {5} Hz{12}Samples/bit: {6}{12}Protocol frames: {7:N0}{12}Duration: {8:0.###} s{12}Frame bytes: {9}{12}Data bytes: {10:N0}{12}Export bytes: {11:N0}",
                        baudRate,
                        dataBits,
                        stopBits,
                        GetParityString(parity),
                        idleBits,
                        sampleRate,
                        previewState.SamplesPerBit,
                        previewState.FrameCount,
                        durationSeconds,
                        ProtocolFrameByteCount,
                        previewState.DataWaveByteCount,
                        previewState.ExportByteCount,
                        Environment.NewLine);
            }
            catch
            {
                GeneratedDataPreviewTextBox.Text = string.Empty;
                HexPreviewTextBox.Text = string.Empty;
                FramePreviewTextBox.Text = string.Empty;
                ProtocolPreviewTextBox.Text = string.Empty;
                InfoTextBlock.Text = "Invalid configuration.";
            }
        }

        private void GenerateBinFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SerialWaveformBuildResult buildResult = BuildWaveform();

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Filter = "BIN files (*.bin)|*.bin|All files (*.*)|*.*",
                    DefaultExt = ".bin",
                    FileName = "serial_protocol_wave.bin"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    byte[] exportData = buildResult.ExportWaveData ?? buildResult.WaveData;
                    File.WriteAllBytes(saveDialog.FileName, exportData);
                    MessageBox.Show(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "BIN file generated.{0}{0}Path: {1}{0}Protocol frames: {2:N0}{0}Bytes: {3:N0}",
                            Environment.NewLine,
                            saveDialog.FileName,
                            buildResult.FrameCount,
                            exportData.Length),
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Generation failed:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool TryBuildWaveform(out SerialWaveformBuildResult buildResult, out string errorMessage)
        {
            buildResult = null;
            errorMessage = null;

            try
            {
                buildResult = BuildWaveform();
                return buildResult != null;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private SerialWaveformBuildResult BuildWaveform()
        {
            uint baudRate = GetBaudRate();
            int dataBits = GetDataBits();
            double stopBitsValue = GetStopBitsValue();
            ParityMode parity = GetParityMode();
            int idleBits = GetIdleBits();
            uint sampleRate = GetSampleRate();
            double durationSeconds = GetDurationSeconds();

            EnsureSupportedProtocolConfiguration(dataBits);

            int samplesPerBit = (int)Math.Round((double)sampleRate / baudRate);
            if (samplesPerBit <= 0)
            {
                throw new InvalidOperationException("Sample rate must be higher than baud rate.");
            }

            long targetTotalSamples = CalculateTargetSampleCount(durationSeconds, sampleRate);
            int samplesPerFrame = CalculateSamplesPerFrame(dataBits, stopBitsValue, parity, idleBits, samplesPerBit);
            long frameCount = CalculateFrameCount(targetTotalSamples, samplesPerFrame);
            if (frameCount <= 0)
            {
                throw new InvalidOperationException("Current duration cannot hold a complete protocol frame.");
            }

            byte[] payloadBytes = BuildProtocolPayloadBytes(GetPayloadSeedText());
            byte[] protocolFrameBytes = BuildProtocolFrameBytes(payloadBytes);
            TwoWireWaveformBuffers buffers = GenerateTwoWireWaveforms(frameCount, targetTotalSamples, protocolFrameBytes, dataBits, stopBitsValue, parity, idleBits, samplesPerBit);

            return new SerialWaveformBuildResult
            {
                WaveData = buffers.DataWaveBytes,
                ClockWaveData = buffers.ClockWaveBytes,
                ExportWaveData = buffers.ExportWaveBytes,
                SampleRate = sampleRate,
                BaudRate = (int)baudRate,
                DataBits = dataBits,
                StopBits = stopBitsValue,
                Parity = parity,
                IdleBits = idleBits,
                InputText = GetPayloadSeedText(),
                FrameCount = frameCount,
                DurationSeconds = durationSeconds
            };
        }

        private static TwoWireWaveformBuffers GenerateTwoWireWaveforms(long frameCount, long targetTotalSamples, byte[] protocolFrameBytes, int dataBits, double stopBits, ParityMode parity, int idleBits, int samplesPerBit)
        {
            if (frameCount <= 0 || targetTotalSamples <= 0 || protocolFrameBytes == null || protocolFrameBytes.Length == 0)
            {
                return new TwoWireWaveformBuffers
                {
                    DataWaveBytes = new byte[0],
                    ClockWaveBytes = new byte[0],
                ExportWaveBytes = new byte[0]
                };
            }

            List<byte> dataSamples = new List<byte>();
            List<byte> clockSamples = new List<byte>();
            int stopSamples = Math.Max(1, (int)Math.Round(stopBits * samplesPerBit));
            int idleSamples = Math.Max(0, idleBits * samplesPerBit);

            for (long frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                if (idleSamples > 0)
                {
                    AppendIdleSamples(dataSamples, clockSamples, 1, idleSamples);
                }

                for (int byteIndex = 0; byteIndex < protocolFrameBytes.Length; byteIndex++)
                {
                    byte data = protocolFrameBytes[byteIndex];
                    AppendClockedSamples(dataSamples, clockSamples, 0, samplesPerBit);

                    int onesCount = 0;
                    for (int bit = 0; bit < dataBits; bit++)
                    {
                        byte value = (byte)((data >> bit) & 0x1);
                        if (value == 1)
                        {
                            onesCount++;
                        }

                        AppendClockedSamples(dataSamples, clockSamples, value, samplesPerBit);
                    }

                    if (parity != ParityMode.None)
                    {
                        AppendClockedSamples(dataSamples, clockSamples, CalculateParityBit(onesCount, parity), samplesPerBit);
                    }

                    AppendClockedSamples(dataSamples, clockSamples, 1, stopSamples);
                }
            }

            long remainingSamples = targetTotalSamples - dataSamples.Count;
            while (remainingSamples > 0)
            {
                int chunkSize = (int)Math.Min(remainingSamples, int.MaxValue);
                AppendIdleSamples(dataSamples, clockSamples, 1, chunkSize);
                remainingSamples -= chunkSize;
            }

            byte[] dataWaveBytes = PackBits(dataSamples);
            byte[] clockWaveBytes = PackBits(clockSamples);
            return new TwoWireWaveformBuffers
            {
                DataWaveBytes = dataWaveBytes,
                ClockWaveBytes = clockWaveBytes,
                ExportWaveBytes = InterleaveClockAndDataBytes(clockWaveBytes, dataWaveBytes)
            };
        }

        private static void AppendIdleSamples(List<byte> dataSamples, List<byte> clockSamples, byte dataValue, int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                dataSamples.Add(dataValue);
                clockSamples.Add(0);
            }
        }

        private static void AppendClockedSamples(List<byte> dataSamples, List<byte> clockSamples, byte dataValue, int sampleCount)
        {
            int lowSamples = sampleCount / 2;

            for (int i = 0; i < sampleCount; i++)
            {
                dataSamples.Add(dataValue);
                clockSamples.Add((byte)(i < lowSamples ? 0 : 1));
            }
        }

        private static byte[] PackBits(IList<byte> bits)
        {
            if (bits == null || bits.Count == 0)
            {
                return new byte[0];
            }

            int byteCount = (bits.Count + 7) / 8;
            byte[] result = new byte[byteCount];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i] == 1)
                {
                    result[i / 8] |= (byte)(1 << (7 - (i % 8)));
                }
            }

            return result;
        }

        private static byte[] InterleaveClockAndDataBytes(byte[] clockWaveBytes, byte[] dataWaveBytes)
        {
            if (clockWaveBytes == null || dataWaveBytes == null || clockWaveBytes.Length != dataWaveBytes.Length)
            {
                return new byte[0];
            }

            byte[] exportBytes = new byte[clockWaveBytes.Length * 2];
            int targetIndex = 0;
            for (int i = 0; i < clockWaveBytes.Length; i++)
            {
                exportBytes[targetIndex++] = clockWaveBytes[i];
                exportBytes[targetIndex++] = dataWaveBytes[i];
            }

            return exportBytes;
        }

        private SerialPreviewState BuildPreviewState(uint baudRate, int dataBits, double stopBits, ParityMode parity, int idleBits, uint sampleRate, double durationSeconds)
        {
            int samplesPerBit = (int)Math.Round((double)sampleRate / baudRate);
            if (samplesPerBit <= 0)
            {
                throw new InvalidOperationException("Sample rate must be higher than baud rate.");
            }

            int samplesPerFrame = CalculateSamplesPerFrame(dataBits, stopBits, parity, idleBits, samplesPerBit);
            long totalSamples = CalculateTargetSampleCount(durationSeconds, sampleRate);
            long frameCount = CalculateFrameCount(totalSamples, samplesPerFrame);
            long dataWaveByteCount = (totalSamples + 7) / 8;
            long exportByteCount = dataWaveByteCount * 2;
            int previewFrameCount = (int)Math.Min(frameCount, PreviewFrameCount);
            string payloadSeedText = GetPayloadSeedText();
            byte[] payloadBytes = BuildProtocolPayloadBytes(payloadSeedText);
            byte[] protocolFrameBytes = BuildProtocolFrameBytes(payloadBytes);
            long previewTotalSamples = Math.Min(totalSamples, Math.Max(1L, previewFrameCount) * (long)samplesPerFrame);
            TwoWireWaveformBuffers previewBuffers = GenerateTwoWireWaveforms(previewFrameCount, previewTotalSamples, protocolFrameBytes, dataBits, stopBits, parity, idleBits, samplesPerBit);

            return new SerialPreviewState
            {
                FrameCount = frameCount,
                SamplesPerBit = samplesPerBit,
                SamplesPerFrame = samplesPerFrame,
                TotalSamples = totalSamples,
                DataWaveByteCount = dataWaveByteCount,
                ExportByteCount = exportByteCount,
                DataPreviewText = BuildGeneratedDataPreview(frameCount, payloadBytes, PreviewCharacterCount),
                DataPreviewHex = BuildGeneratedDataHexPreview(frameCount, payloadBytes, PreviewCharacterCount),
                FramePreviewText = BuildFramePreview(frameCount, payloadBytes),
                ProtocolPreviewText = BuildProtocolPreview(protocolFrameBytes, dataWaveByteCount, exportByteCount, previewBuffers.ExportWaveBytes)
            };
        }

        private static string BuildGeneratedDataPreview(long frameCount, byte[] payloadBytes, int previewCharacterCount)
        {
            if (frameCount <= 0)
            {
                return "No complete protocol frame can be generated with the current duration.";
            }

            byte[] previewBytes = GetPreviewBytes(payloadBytes, previewCharacterCount);
            int previewLength = previewBytes.Length;
            StringBuilder builder = new StringBuilder(previewLength + 32);
            for (int i = 0; i < previewLength; i++)
            {
                builder.Append(ConvertByteToPreviewCharacter(previewBytes[i]));
            }

            if (payloadBytes != null && payloadBytes.Length > previewLength)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }

        private static string BuildGeneratedDataHexPreview(long frameCount, byte[] payloadBytes, int previewCharacterCount)
        {
            if (frameCount <= 0)
            {
                return string.Empty;
            }

            byte[] previewBytes = GetPreviewBytes(payloadBytes, previewCharacterCount);
            string hex = ConvertBytesToHex(previewBytes);
            if (payloadBytes != null && payloadBytes.Length > previewBytes.Length)
            {
                return hex + " ...";
            }

            return hex;
        }

        private static string BuildFramePreview(long frameCount, byte[] payloadBytes)
        {
            if (frameCount <= 0)
            {
                return "No frame preview is available.";
            }

            ushort checksum = CalculateProtocolChecksum(payloadBytes);

            StringBuilder builder = new StringBuilder();
            int previewFrames = (int)Math.Min(frameCount, PreviewFrameCount);
            for (int frameIndex = 0; frameIndex < previewFrames; frameIndex++)
            {
                if (frameIndex > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendFormat(CultureInfo.InvariantCulture, "Frame {0:D4}  TotalBytes={1}", frameIndex, ProtocolFrameByteCount);
                builder.AppendLine();
                builder.AppendFormat(CultureInfo.InvariantCulture, "Header={0}", ConvertBytesToHex(BuildUInt32BigEndianBytes(ProtocolFrameHeader)));
                builder.AppendLine();
                builder.AppendFormat(CultureInfo.InvariantCulture, "Length={0}", ConvertBytesToHex(BuildUInt16BigEndianBytes(ProtocolDataLength)));
                builder.AppendLine();
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Type={0}  Address={1}",
                    ConvertBytesToHex(BuildUInt16BigEndianBytes(ProtocolDataType)),
                    ConvertBytesToHex(BuildUInt16BigEndianBytes(ProtocolInterfaceAddress)));
                builder.AppendLine();
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Payload[{0}]={1}",
                    payloadBytes.Length,
                    ConvertBytesToHex(GetPreviewBytes(payloadBytes, 16)));
                if (payloadBytes.Length > 16)
                {
                    builder.Append(" ...");
                }

                builder.AppendLine();
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Checksum={0} (length + type + address + payload byte sum)",
                    ConvertBytesToHex(BuildUInt16BigEndianBytes(checksum)));
                builder.AppendLine();
                builder.AppendFormat(CultureInfo.InvariantCulture, "Tail={0}", ConvertBytesToHex(BuildUInt32BigEndianBytes(ProtocolFrameTail)));
            }

            if (frameCount > previewFrames)
            {
                builder.AppendLine();
                builder.Append("...");
            }

            return builder.ToString();
        }

        private static string BuildProtocolPreview(byte[] frameBytes, long dataWaveByteCount, long exportByteCount, byte[] previewExportWaveBytes)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Protocol frame format: Header(4) + Length(2) + Type(2) + Address(2) + Payload(1016) + Checksum(2) + Tail(4)");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "FrameBytes={0}  WaveBytes={1:N0}  ExportBytes={2:N0}",
                frameBytes == null ? 0 : frameBytes.Length,
                dataWaveByteCount,
                exportByteCount);

            if (frameBytes == null || frameBytes.Length == 0)
            {
                builder.AppendLine();
                builder.Append("No protocol frame is available.");
                return builder.ToString();
            }

            builder.AppendLine();
            builder.AppendLine("First frame bytes:");
            builder.Append(ConvertBytesToHex(GetPreviewBytes(frameBytes, PreviewProtocolByteCount)));
            if (frameBytes.Length > PreviewProtocolByteCount)
            {
                builder.Append(" ...");
            }

            if (previewExportWaveBytes != null && previewExportWaveBytes.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("Preview exported waveform bytes (CLK/DATA interleaved, sampled at the configured sample rate):");
                builder.Append(ConvertBytesToHex(GetPreviewBytes(previewExportWaveBytes, PreviewProtocolByteCount)));
                if (previewExportWaveBytes.Length > PreviewProtocolByteCount)
                {
                    builder.Append(" ...");
                }
            }

            return builder.ToString();
        }

        private static byte CalculateParityBit(int onesCount, ParityMode parity)
        {
            switch (parity)
            {
                case ParityMode.Odd:
                    return (byte)(onesCount % 2 == 0 ? 1 : 0);
                case ParityMode.Even:
                    return (byte)(onesCount % 2 == 0 ? 0 : 1);
                case ParityMode.Mark:
                    return 1;
                case ParityMode.Space:
                    return 0;
                default:
                    return 0;
            }
        }

        private static string ConvertBytesToHex(IList<byte> bytes)
        {
            if (bytes == null || bytes.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(bytes.Count * 3);
            for (int i = 0; i < bytes.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static int CalculateSamplesPerFrame(int dataBits, double stopBits, ParityMode parity, int idleBits, int samplesPerBit)
        {
            int parityBitCount = parity == ParityMode.None ? 0 : 1;
            int stopSamples = Math.Max(1, (int)Math.Round(stopBits * samplesPerBit));
            int samplesPerByte = ((1 + dataBits + parityBitCount) * samplesPerBit) + stopSamples;
            return idleBits * samplesPerBit + (ProtocolFrameByteCount * samplesPerByte);
        }

        private static long CalculateTargetSampleCount(double durationSeconds, uint sampleRate)
        {
            if (durationSeconds <= 0 || sampleRate == 0)
            {
                return 0;
            }

            return (long)Math.Round(durationSeconds * sampleRate, MidpointRounding.AwayFromZero);
        }

        private static long CalculateFrameCount(long targetTotalSamples, int samplesPerFrame)
        {
            if (targetTotalSamples <= 0 || samplesPerFrame <= 0)
            {
                return 0;
            }

            return targetTotalSamples / samplesPerFrame;
        }

        private static byte[] BuildProtocolFrameBytes(byte[] payloadBytes)
        {
            byte[] lengthBytes = BuildUInt16BigEndianBytes(ProtocolDataLength);
            byte[] typeBytes = BuildUInt16BigEndianBytes(ProtocolDataType);
            byte[] addressBytes = BuildUInt16BigEndianBytes(ProtocolInterfaceAddress);
            byte[] checksumBytes = BuildUInt16BigEndianBytes(CalculateProtocolChecksum(payloadBytes));

            List<byte> frameBytes = new List<byte>(ProtocolFrameByteCount);
            frameBytes.AddRange(BuildUInt32BigEndianBytes(ProtocolFrameHeader));
            frameBytes.AddRange(lengthBytes);
            frameBytes.AddRange(typeBytes);
            frameBytes.AddRange(addressBytes);
            frameBytes.AddRange(payloadBytes);
            frameBytes.AddRange(checksumBytes);
            frameBytes.AddRange(BuildUInt32BigEndianBytes(ProtocolFrameTail));
            return frameBytes.ToArray();
        }

        private static byte[] BuildProtocolPayloadBytes(string payloadSeedText)
        {
            string normalizedSeedText = string.IsNullOrEmpty(payloadSeedText) ? DefaultPayloadSeedText : payloadSeedText;
            byte[] seedBytes = Encoding.ASCII.GetBytes(normalizedSeedText);
            if (seedBytes.Length == 0)
            {
                seedBytes = Encoding.ASCII.GetBytes(DefaultPayloadSeedText);
            }

            byte[] payloadBytes = new byte[ProtocolPayloadLength];
            for (int i = 0; i < payloadBytes.Length; i++)
            {
                payloadBytes[i] = seedBytes[i % seedBytes.Length];
            }

            return payloadBytes;
        }

        private static ushort CalculateProtocolChecksum(byte[] payloadBytes)
        {
            byte[] lengthBytes = BuildUInt16BigEndianBytes(ProtocolDataLength);
            byte[] typeBytes = BuildUInt16BigEndianBytes(ProtocolDataType);
            byte[] addressBytes = BuildUInt16BigEndianBytes(ProtocolInterfaceAddress);

            int sum = 0;
            AccumulateByteSum(lengthBytes, ref sum);
            AccumulateByteSum(typeBytes, ref sum);
            AccumulateByteSum(addressBytes, ref sum);
            AccumulateByteSum(payloadBytes, ref sum);

            return (ushort)(sum & 0xFFFF);
        }

        private static void AccumulateByteSum(byte[] bytes, ref int sum)
        {
            if (bytes == null)
            {
                return;
            }

            for (int i = 0; i < bytes.Length; i++)
            {
                sum += bytes[i];
            }
        }

        private static byte[] BuildUInt16BigEndianBytes(ushort value)
        {
            return new byte[]
            {
                (byte)(value >> 8),
                (byte)(value & 0xFF)
            };
        }

        private static byte[] BuildUInt32BigEndianBytes(uint value)
        {
            return new byte[]
            {
                (byte)(value >> 24),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        private static byte[] GetPreviewBytes(byte[] source, int count)
        {
            if (source == null || source.Length == 0 || count <= 0)
            {
                return new byte[0];
            }

            int previewLength = Math.Min(source.Length, count);
            byte[] preview = new byte[previewLength];
            Array.Copy(source, preview, previewLength);
            return preview;
        }

        private static char ConvertByteToPreviewCharacter(byte value)
        {
            return value >= 0x20 && value <= 0x7E ? (char)value : '.';
        }

        private static void EnsureSupportedProtocolConfiguration(int dataBits)
        {
            if (dataBits != 8)
            {
                throw new InvalidOperationException("The current protocol generator requires 8 data bits.");
            }
        }

        private uint GetBaudRate()
        {
            ComboBoxItem item = BaudRateComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            uint value;
            if (content != null && uint.TryParse(content, out value))
            {
                return value;
            }

            return 19200;
        }

        private int GetDataBits()
        {
            ComboBoxItem item = DataBitsComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            int value;
            if (content != null && int.TryParse(content, out value))
            {
                return value;
            }

            return 8;
        }

        private double GetStopBitsValue()
        {
            ComboBoxItem item = StopBitsComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            double value;
            if (content != null && TryParseDouble(content, out value))
            {
                return value;
            }

            return 1;
        }

        private ParityMode GetParityMode()
        {
            switch (ParityComboBox.SelectedIndex)
            {
                case 1:
                    return ParityMode.Odd;
                case 2:
                    return ParityMode.Even;
                case 3:
                    return ParityMode.Mark;
                case 4:
                    return ParityMode.Space;
                default:
                    return ParityMode.None;
            }
        }

        private int GetIdleBits()
        {
            int value;
            if (int.TryParse(IdleBitsTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
            {
                return value;
            }

            if (int.TryParse(IdleBitsTextBox.Text, out value) && value >= 0)
            {
                return value;
            }

            return 1;
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
            if (TryParseDouble(DurationSecondsTextBox.Text, out value) && value > 0)
            {
                return value;
            }

            throw new InvalidOperationException("Duration must be a positive number.");
        }

        private string GetPayloadSeedText()
        {
            string text = PayloadSeedTextBox == null ? null : PayloadSeedTextBox.Text;
            return string.IsNullOrEmpty(text) ? DefaultPayloadSeedText : text;
        }

        private static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static string GetParityString(ParityMode mode)
        {
            switch (mode)
            {
                case ParityMode.Odd:
                    return "Odd";
                case ParityMode.Even:
                    return "Even";
                case ParityMode.Mark:
                    return "Mark";
                case ParityMode.Space:
                    return "Space";
                default:
                    return "None";
            }
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
