using System;
using System.Collections.Generic;
using System.Threading;

namespace InteractiveExamples
{
    internal sealed class SignalProducer
    {
        private readonly object _syncRoot = new object();
        private double _nextSampleTimeSeconds;
        private int _appendCountPerRound;
        private double _sampleIntervalSeconds;

        public object SyncRoot
        {
            get
            {   
                return _syncRoot;
            }
        }

        public void Reset(IReadOnlyList<ChartSignal> chartSignals, int appendCountPerRound, double sampleIntervalSeconds)
        {
            _appendCountPerRound = appendCountPerRound;
            _sampleIntervalSeconds = sampleIntervalSeconds;
            _nextSampleTimeSeconds = 0;
            ClearPendingData(chartSignals);

            int seedBase = Environment.TickCount;
            for (int seriesIndex = 0; seriesIndex < chartSignals.Count; seriesIndex++)
            {
                chartSignals[seriesIndex].ResetRuntimeState(new Random(seedBase + seriesIndex * 97), 0.5);
            }
        }

        public void EnqueueSignalData(IReadOnlyList<ChartSignal> chartSignals)
        {
            GenerateSignalData(chartSignals);
        }

        public void ClearPendingData(IReadOnlyList<ChartSignal> chartSignals)
        {
            lock (_syncRoot)
            {
                for (int i = 0; i < chartSignals.Count; i++)
                {
                    chartSignals[i].ClearPendingChunks();
                }
            }
        }

        private void GenerateSignalData(IReadOnlyList<ChartSignal> chartSignals)
        {
            double[] sampleTimes = new double[_appendCountPerRound];

            for (int pointIndex = 0; pointIndex < _appendCountPerRound; pointIndex++)
            {
                sampleTimes[pointIndex] = _nextSampleTimeSeconds;
                _nextSampleTimeSeconds += _sampleIntervalSeconds;
            }

            for (int i = 0; i < chartSignals.Count; i++)
            {
                ChartSignal signal = chartSignals[i];
                var points = signal.CreatePoints(sampleTimes);
                if (points != null)
                {
                    signal.EnqueuePoints(points);
                }
            }
        }
    }
}
