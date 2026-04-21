using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;

namespace InteractiveExamples
{
    internal sealed class DigitalHistorySnapshot
    {
        public static readonly DigitalHistorySnapshot Empty = new DigitalHistorySnapshot(new uint[0], 0, 0);

        public DigitalHistorySnapshot(uint[] digitalWords, int sampleCount, double sampleInterval)
        {
            DigitalWords = digitalWords ?? new uint[0];
            SampleCount = sampleCount;
            SampleInterval = sampleInterval;
        }

        public uint[] DigitalWords { get; private set; }
        public int SampleCount { get; private set; }
        public double SampleInterval { get; private set; }
    }

    internal sealed class ChartSignal
    {
        private readonly object _historySync = new object();
        private DigitalHistorySnapshot _history = DigitalHistorySnapshot.Empty;
        private int _historyVersion;

        public ChartSignal(string name)
        {
            Name = name;
            DecodeSettings = new UartDecodeSettings();
        }

        public string Name { get; set; }
        public AxisY AxisY { get; set; }
        public DigitalLineSeries Series { get; set; }
        public System.Windows.Media.Color SeriesColor { get; set; }
        public UartDecodeSettings DecodeSettings { get; private set; }
        public int HistoryVersion
        {
            get
            {
                lock (_historySync)
                {
                    return _historyVersion;
                }
            }
        }

        public void SetDigitalHistory(uint[] digitalWords, int sampleCount, double sampleInterval)
        {
            if (digitalWords == null || sampleCount <= 0 || sampleInterval <= 0)
            {
                return;
            }

            lock (_historySync)
            {
                _history = new DigitalHistorySnapshot(digitalWords, sampleCount, sampleInterval);
                _historyVersion++;
            }
        }

        public DigitalHistorySnapshot GetDigitalHistorySnapshot()
        {
            lock (_historySync)
            {
                return _history;
            }
        }

        public void ClearDigitalHistory()
        {
            lock (_historySync)
            {
                if (_history.SampleCount > 0)
                {
                    _historyVersion++;
                }

                _history = DigitalHistorySnapshot.Empty;
            }
        }
    }
}
