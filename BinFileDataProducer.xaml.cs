using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using InteractiveExamples;

namespace LCWpf
{
    public partial class BinFileDataProducer : UserControl
    {
        private const int HexBytesPerLine = 16;

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
            MessageBox.Show(
                "The UI is ready. BIN generation backend will be implemented later.",
                "Pending",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
