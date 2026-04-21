using System;

namespace InteractiveExamples
{
    internal sealed class UartDecodeSettings
    {
        private int _version;
        private int _baudRate = 19200;
        private int _dataBits = 8;
        private double _stopBits = 1;
        private UartParityMode _parityMode = UartParityMode.None;
        private int _idleBits = 1;
        private SignalDecodeMode _mode = SignalDecodeMode.UartFrame;

        public int Version
        {
            get
            {
                return _version;
            }
        }

        public int BaudRate
        {
            get
            {
                return _baudRate;
            }
            set
            {
                int normalizedValue = Math.Max(1, value);
                if (_baudRate != normalizedValue)
                {
                    _baudRate = normalizedValue;
                    _version++;
                }
            }
        }

        public int DataBits
        {
            get
            {
                return _dataBits;
            }
            set
            {
                int normalizedValue = Math.Max(1, value);
                if (_dataBits != normalizedValue)
                {
                    _dataBits = normalizedValue;
                    _version++;
                }
            }
        }

        public double StopBits
        {
            get
            {
                return _stopBits;
            }
            set
            {
                double normalizedValue = value <= 0 ? 1 : value;
                if (Math.Abs(_stopBits - normalizedValue) > double.Epsilon)
                {
                    _stopBits = normalizedValue;
                    _version++;
                }
            }
        }

        public UartParityMode ParityMode
        {
            get
            {
                return _parityMode;
            }
            set
            {
                if (_parityMode != value)
                {
                    _parityMode = value;
                    _version++;
                }
            }
        }

        public int IdleBits
        {
            get
            {
                return _idleBits;
            }
            set
            {
                int normalizedValue = Math.Max(0, value);
                if (_idleBits != normalizedValue)
                {
                    _idleBits = normalizedValue;
                    _version++;
                }
            }
        }

        public SignalDecodeMode Mode
        {
            get
            {
                return _mode;
            }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    _version++;
                }
            }
        }
    }
}
