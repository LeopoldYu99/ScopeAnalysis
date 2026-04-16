using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LCWpf
{
    /// <summary>
    /// SerialPortDataProducer.xaml 的交互逻辑
    /// </summary>
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
                var baudRate = GetBaudRate();
                var dataBits = GetDataBits();
                var stopBits = GetStopBitsValue();
                var parity = GetParityMode();
                var idleBits = GetIdleBits();
                var sampleRate = GetSampleRate();
                var inputText = StringInputTextBox.Text ?? "";
                var samplesPerBit = (double)sampleRate / baudRate;
                var samplesPerBitInt = (int)Math.Round(samplesPerBit);

                int bitsPerFrame = 1 + dataBits;
                if (parity != ParityMode.None)
                {
                    bitsPerFrame += 1;
                }
                bitsPerFrame += (int)Math.Round(stopBits);

                var totalFrames = inputText.Length;
                var totalBits = idleBits + totalFrames * bitsPerFrame;
                var totalSamples = (long)totalBits * samplesPerBitInt;
                var totalBytes = (totalSamples + 7) / 8;

                var info = $"波特率: {baudRate} bps\n" +
                           $"数据位: {dataBits}\n" +
                           $"停止位: {stopBits}\n" +
                           $"校验位: {GetParityString(parity)}\n" +
                           $"空闲位: {idleBits}\n" +
                           $"采样率: {sampleRate} Hz\n" +
                           $"每位采样点: {samplesPerBitInt}\n" +
                           $"每帧位数: {bitsPerFrame}\n" +
                           $"发送字符: {inputText.Length}\n" +
                           $"总采样点: {totalSamples:N0}\n" +
                           $"文件大小: {totalBytes:N0} 字节";

                InfoTextBlock.Text = info;
            }
            catch
            {
                InfoTextBlock.Text = "配置有误，请检查输入";
            }
        }

        private void GenerateBinFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baudRate = GetBaudRate();
                var dataBits = GetDataBits();
                var stopBitsValue = GetStopBitsValue();
                var parity = GetParityMode();
                var idleBits = GetIdleBits();
                var sampleRate = GetSampleRate();
                var inputText = StringInputTextBox.Text ?? "";
                var samplesPerBit = (int)Math.Round((double)sampleRate / baudRate);

                var waveData = GenerateWaveform(inputText, dataBits, stopBitsValue, parity, idleBits, samplesPerBit);

                var saveDialog = new SaveFileDialog
                {
                    Filter = "BIN文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                    DefaultExt = ".bin",
                    FileName = "serial_wave.bin"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDialog.FileName, waveData);
                    MessageBox.Show(
                        $"BIN文件已生成!\n\n路径: {saveDialog.FileName}\n采样点数: {waveData.Length:N0}\n文件大小: {waveData.Length:N0} 字节",
                        "成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"生成失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private byte[] GenerateWaveform(string text, int dataBits, double stopBits, ParityMode parity, int idleBits, int samplesPerBit)
        {
            var bits = new List<byte>();

            for (var i = 0; i < idleBits * samplesPerBit; i++)
            {
                bits.Add(1);
            }

            foreach (char c in text)
            {
                var data = (byte)(c & ((1 << dataBits) - 1));

                for (var i = 0; i < samplesPerBit; i++)
                {
                    bits.Add(0);
                }

                var onesCount = 0;
                for (var bit = 0; bit < dataBits; bit++)
                {
                    var val = (byte)((data >> bit) & 1);
                    if (val == 1)
                    {
                        onesCount++;
                    }

                    for (var i = 0; i < samplesPerBit; i++)
                    {
                        bits.Add(val);
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

                    for (var i = 0; i < samplesPerBit; i++)
                    {
                        bits.Add(parityBit);
                    }
                }

                var stopSamples = (int)Math.Round(stopBits * samplesPerBit);
                for (var i = 0; i < stopSamples; i++)
                {
                    bits.Add(1);
                }
            }

            var byteCount = (bits.Count + 7) / 8;
            var result = new byte[byteCount];
            for (var i = 0; i < bits.Count; i++)
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
            var item = BaudRateComboBox.SelectedItem as ComboBoxItem;
            var content = item != null ? item.Content as string : null;
            uint value;
            if (content != null && uint.TryParse(content, out value))
            {
                return value;
            }

            return 19200;
        }

        private int GetDataBits()
        {
            var item = DataBitsComboBox.SelectedItem as ComboBoxItem;
            var content = item != null ? item.Content as string : null;
            int value;
            if (content != null && int.TryParse(content, out value))
            {
                return value;
            }

            return 8;
        }

        private double GetStopBitsValue()
        {
            var item = StopBitsComboBox.SelectedItem as ComboBoxItem;
            var content = item != null ? item.Content as string : null;
            double value;
            if (content != null && double.TryParse(content, out value))
            {
                return value;
            }

            return 1;
        }

        private ParityMode GetParityMode()
        {
            switch (ParityComboBox.SelectedIndex)
            {
                case 0:
                    return ParityMode.None;
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
            if (int.TryParse(IdleBitsTextBox.Text, out value) && value >= 0)
            {
                return value;
            }

            return 1;
        }

        private uint GetSampleRate()
        {
            uint value;
            if (uint.TryParse(SampleRateTextBox.Text, out value) && value > 0)
            {
                return value;
            }

            return 1000000;
        }

        private string GetParityString(ParityMode mode)
        {
            switch (mode)
            {
                case ParityMode.None:
                    return "None";
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
