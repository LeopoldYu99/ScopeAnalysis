using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using System.Collections.Generic;

namespace InteractiveExamples
{
    internal sealed class ChartSignal
    {
        private readonly LinkedList<SeriesPoint[]> _pendingChunks = new LinkedList<SeriesPoint[]>();
        private readonly object _pendingSync = new object();
        private readonly LinkedList<SeriesPoint> _recentPoints = new LinkedList<SeriesPoint>();
        private readonly object _historySync = new object();
        private int _historyVersion;

        public ChartSignal(string name)
        {
            Name = name;
            DecodeSettings = new UartDecodeSettings();
        }

        public string Name { get; private set; }
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


        public void AppendRecentPoints(SeriesPoint[] points)
        {
            if (points == null || points.Length == 0)
            {
                return;
            }

            lock (_historySync)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    _recentPoints.AddLast(points[i]);
                }

                _historyVersion++;
            }
        }



        public SeriesPoint[] GetAllRecentPointsSnapshot()
        {
            lock (_historySync)
            {
                SeriesPoint[] snapshot = new SeriesPoint[_recentPoints.Count];
                _recentPoints.CopyTo(snapshot, 0);
                return snapshot;
            }
        }


        public void ClearRecentPoints()
        {
            lock (_historySync)
            {
                if (_recentPoints.Count > 0)
                {
                    _historyVersion++;
                }

                _recentPoints.Clear();
            }
        }


    }
}
