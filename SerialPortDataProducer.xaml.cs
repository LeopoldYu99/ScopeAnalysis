using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LCWpf
{
    public sealed class SerialWaveformBuildResult
    {
        public byte[] WaveData { get; set; }
        public uint SampleRate { get; set; }
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public double StopBits { get; set; }
        public ParityMode Parity { get; set; }
        public int IdleBits { get; set; }
        public string InputText { get; set; }
    }

    public partial class SerialPortDataProducer : UserControl
    {
        public SerialPortDataProducer()
        {
            InitializeComponent();
            BaudRateComboBox.SelectionChanged += UpdatePreview;
            DataBitsComboBox.SelectionChanged += UpdatePreview;
            StopBitsComboBox.SelectionChanged += UpdatePreview;
            ParityComboBox.SelectionChanged += UpdatePreview;
            IdleBitsTextBox.TextChanged += UpdatePreview;
            SampleRateTextBox.TextChanged += UpdatePreview;
            StringInputTextBox.TextChanged += UpdatePreview;
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
                string inputText = StringInputTextBox.Text ?? string.Empty;
                double samplesPerBit = (double)sampleRate / baudRate;
                int samplesPerBitInt = (int)Math.Round(samplesPerBit);

                int bitsPerFrame = 1 + dataBits + (parity != ParityMode.None ? 1 : 0) + (int)Math.Round(stopBits);
                int totalFrames = inputText.Length;
                long totalBits = (long)totalFrames * (idleBits + bitsPerFrame);
                long totalSamples = totalBits * samplesPerBitInt;
                long totalBytes = (totalSamples + 7) / 8;

                InfoTextBlock.Text =
                    $"Baud rate: {baudRate} bps\n" +
                    $"Data bits: {dataBits}\n" +
                    $"Stop bits: {stopBits}\n" +
                    $"Parity: {GetParityString(parity)}\n" +
                    $"Idle bits: {idleBits}\n" +
                    $"Sample rate: {sampleRate} Hz\n" +
                    $"Samples/bit: {samplesPerBitInt}\n" +
                    $"Bits/frame: {bitsPerFrame}\n" +
                    $"Chars: {inputText.Length}\n" +
                    $"Samples: {totalSamples:N0}\n" +
                    $"Bytes: {totalBytes:N0}";
            }
            catch
            {
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
                    FileName = "serial_wave.bin"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDialog.FileName, buildResult.WaveData);
                    MessageBox.Show(
                        $"BIN file generated.\n\nPath: {saveDialog.FileName}\nBytes: {buildResult.WaveData.Length:N0}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Generation failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            string inputText = StringInputTextBox.Text ?? string.Empty;
            int samplesPerBit = (int)Math.Round((double)sampleRate / baudRate);

            return new SerialWaveformBuildResult
            {
                WaveData = GenerateWaveform(inputText, dataBits, stopBitsValue, parity, idleBits, samplesPerBit),
                SampleRate = sampleRate,
                BaudRate = (int)baudRate,
                DataBits = dataBits,
                StopBits = stopBitsValue,
                Parity = parity,
                IdleBits = idleBits,
                InputText = inputText
            };
        }

        private byte[] GenerateWaveform(string text, int dataBits, double stopBits, ParityMode parity, int idleBits, int samplesPerBit)
        {
            List<byte> bits = new List<byte>();

            foreach (char character in text)
            {
                for (int i = 0; i < idleBits * samplesPerBit; i++)
                {
                    bits.Add(1);
                }

                byte data = (byte)(character & ((1 << dataBits) - 1));

                for (int i = 0; i < samplesPerBit; i++)
                {
                    bits.Add(0);
                }

                int onesCount = 0;
                for (int bit = 0; bit < dataBits; bit++)
                {
                    byte value = (byte)((data >> bit) & 1);
                    if (value == 1)
                    {
                        onesCount++;
                    }

                    for (int i = 0; i < samplesPerBit; i++)
                    {
                        bits.Add(value);
                    }
                }

                if (parity != ParityMode.None)
                {
                    byte parityBit;
                    switch (parity)
                    {
                        case ParityMode.Odd:
                            parityBit = (byte)(onesCount % 2 == 0 ? 1 : 0);
                            break;
                        case ParityMode.Even:
                            parityBit = (byte)(onesCount % 2 == 0 ? 0 : 1);
                            break;
                        case ParityMode.Mark:
                            parityBit = 1;
                            break;
                        case ParityMode.Space:
                            parityBit = 0;
                            break;
                        default:
                            parityBit = 0;
                            break;
                    }

                    for (int i = 0; i < samplesPerBit; i++)
                    {
                        bits.Add(parityBit);
                    }
                }

                int stopSamples = (int)Math.Round(stopBits * samplesPerBit);
                for (int i = 0; i < stopSamples; i++)
                {
                    bits.Add(1);
                }
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

        private uint GetBaudRate()
        {
            ComboBoxItem item = BaudRateComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            if (content != null && uint.TryParse(content, out uint value))
            {
                return value;
            }

            return 19200;
        }

        private int GetDataBits()
        {
            ComboBoxItem item = DataBitsComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            if (content != null && int.TryParse(content, out int value))
            {
                return value;
            }

            return 8;
        }

        private double GetStopBitsValue()
        {
            ComboBoxItem item = StopBitsComboBox.SelectedItem as ComboBoxItem;
            string content = item != null ? item.Content as string : null;
            if (content != null && double.TryParse(content, out double value))
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
            if (int.TryParse(IdleBitsTextBox.Text, out int value) && value >= 0)
            {
                return value;
            }

            return 1;
        }

        private uint GetSampleRate()
        {
            if (uint.TryParse(SampleRateTextBox.Text, out uint value) && value > 0)
            {
                return value;
            }

            return 1000000;
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
