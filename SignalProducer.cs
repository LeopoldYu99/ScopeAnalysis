using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace InteractiveExamples
{
    internal sealed class SignalProducer
    {
        private readonly ConcurrentQueue<double> _pendingRoundEndTimes = new ConcurrentQueue<double>();
        private readonly object _syncRoot = new object();
        private double _nextSampleTimeSeconds;
        private int _appendCountPerRound;
        private double _sampleIntervalSeconds;
        private long _pendingRoundCount;
        private long _pendingSampleCount;

        public object SyncRoot
        {
            get
            {
                return _syncRoot;
            }
        }

        public long PendingRoundCount
        {
            get
            {
                return Interlocked.Read(ref _pendingRoundCount);
            }
        }

        public long PendingSampleCount
        {
            get
            {
                return Interlocked.Read(ref _pendingSampleCount);
            }
        }

        public void Reset(IReadOnlyList<ChartSignal> chartSignals, int appendCountPerRound, double sampleIntervalSeconds)
        {
            _appendCountPerRound = appendCountPerRound;
            _sampleIntervalSeconds = sampleIntervalSeconds;
            _nextSampleTimeSeconds = 0;
            ClearPendingSignalRounds(chartSignals);

            int seedBase = Environment.TickCount;
            for (int seriesIndex = 0; seriesIndex < chartSignals.Count; seriesIndex++)
            {
                chartSignals[seriesIndex].ResetRuntimeState(new Random(seedBase + seriesIndex * 97), 50);
            }
        }

        public void EnqueueSignalRound(IReadOnlyList<ChartSignal> chartSignals)
        {
            double lastTime = GenerateSignalRound(chartSignals, true);
            _pendingRoundEndTimes.Enqueue(lastTime);
            Interlocked.Increment(ref _pendingRoundCount);
            Interlocked.Add(ref _pendingSampleCount, _appendCountPerRound);
        }

        public double GenerateSignalRoundDirect(IReadOnlyList<ChartSignal> chartSignals)
        {
            return GenerateSignalRound(chartSignals, false);
        }

        public bool TryDequeuePendingRound(out double lastRoundX)
        {
            if (_pendingRoundEndTimes.TryDequeue(out lastRoundX) == false)
            {
                return false;
            }

            Interlocked.Decrement(ref _pendingRoundCount);
            Interlocked.Add(ref _pendingSampleCount, -_appendCountPerRound);
            return true;
        }

        public void ClearPendingSignalRounds(IReadOnlyList<ChartSignal> chartSignals)
        {
            lock (_syncRoot)
            {
                double lastRoundX;
                while (_pendingRoundEndTimes.TryDequeue(out lastRoundX))
                {
                }

                for (int i = 0; i < chartSignals.Count; i++)
                {
                    chartSignals[i].ClearPendingChunks();
                }
            }

            Interlocked.Exchange(ref _pendingRoundCount, 0);
            Interlocked.Exchange(ref _pendingSampleCount, 0);
        }

        private double GenerateSignalRound(IReadOnlyList<ChartSignal> chartSignals, bool enqueueToBuffers)
        {
            double[] sampleTimes = new double[_appendCountPerRound];
            double lastTime = _nextSampleTimeSeconds;

            for (int pointIndex = 0; pointIndex < _appendCountPerRound; pointIndex++)
            {
                sampleTimes[pointIndex] = _nextSampleTimeSeconds;
                lastTime = _nextSampleTimeSeconds;
                _nextSampleTimeSeconds += _sampleIntervalSeconds;
            }

            for (int i = 0; i < chartSignals.Count; i++)
            {
                ChartSignal signal = chartSignals[i];
                var points = signal.CreatePoints(sampleTimes);
                if (enqueueToBuffers)
                {
                    signal.EnqueuePoints(points);
                }
                else if (points.Length > 0)
                {
                    signal.Series.AddPoints(points, false);
                }
            }

            return lastTime;
        }
    }
}
