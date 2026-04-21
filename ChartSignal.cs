using Arction.Wpf.Charting;
using Arction.Wpf.Charting.Axes;
using Arction.Wpf.Charting.SeriesXY;
using System;

namespace InteractiveExamples
{
    internal sealed class ChartSignal
    {
        private readonly object _historySync = new object();
        private SeriesPoint[] _recentPoints = new SeriesPoint[0];
        private int _historyVersion;

        public ChartSignal(string name)
        {
            Name = name;
            DecodeSettings = new UartDecodeSettings();
        }

        public string Name { get;  set; }
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
                if (_recentPoints.Length == 0)
                {
                    _recentPoints = points;
                }
                else
                {
                    SeriesPoint[] merged = new SeriesPoint[_recentPoints.Length + points.Length];
                    Array.Copy(_recentPoints, 0, merged, 0, _recentPoints.Length);
                    Array.Copy(points, 0, merged, _recentPoints.Length, points.Length);
                    _recentPoints = merged;
                }

                _historyVersion++;
            }
        }



        public SeriesPoint[] GetAllRecentPointsSnapshot()
        {
            lock (_historySync)
            {
                return _recentPoints;
            }
        }


        public void ClearRecentPoints()
        {
            lock (_historySync)
            {
                if (_recentPoints.Length > 0)
                {
                    _historyVersion++;
                }

                _recentPoints = new SeriesPoint[0];
            }
        }


    }
}
