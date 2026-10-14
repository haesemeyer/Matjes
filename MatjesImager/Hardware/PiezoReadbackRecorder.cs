using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// Records a fixed number of piezo read-back samples from the start of a scan, then keeps draining the queue
    /// so that the analog-in stream does not overflow while the scan continues.
    /// Progress and results are polled (e.g. from a UI timer), nothing is raised on the consumer thread
    /// </summary>
    public class PiezoReadbackRecorder : IAnalogProcessor<Tuple<long, double>>, IDisposable
    {
        private readonly double[] _samples;

        /// <summary>
        /// Written by the consumer thread only, read by others
        /// </summary>
        private volatile int _numRecorded = 0;

        private volatile bool _hasGap = false;

        private CancellationTokenSource? _cancellationTokenSource;

        private System.Threading.Tasks.Task? _consumeTask;

        /// <param name="samplesToRecord">The number of read-back samples to record (after averaging in the AIStream)</param>
        public PiezoReadbackRecorder(int samplesToRecord)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(samplesToRecord, 1);
            _samples = new double[samplesToRecord];
        }

        /// <summary>
        /// The number of samples that will be recorded
        /// </summary>
        public int SamplesToRecord => _samples.Length;

        /// <summary>
        /// The number of samples recorded so far
        /// </summary>
        public int NumRecorded => _numRecorded;

        /// <summary>
        /// Indicates that all requested samples were recorded
        /// </summary>
        public bool IsComplete => _numRecorded >= _samples.Length;

        /// <summary>
        /// Indicates that samples were lost (e.g. through a queue overflow) so that the recording is no longer aligned with the scan.
        /// Recording stops when this happens
        /// </summary>
        public bool HasGap => _hasGap;

        /// <summary>
        /// Returns a copy of the recorded samples (in volts) once recording is complete, null before
        /// </summary>
        public double[]? GetSamples()
        {
            if (!IsComplete)
                return null;
            return (double[])_samples.Clone();
        }

        private void ConsumeLoop(CancellationToken token, ProducerConsumer<Tuple<long, double>> queue)
        {
            while (!token.IsCancellationRequested)
            {
                Tuple<long, double> sample;
                try
                {
                    sample = queue.Consume(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                int n = _numRecorded;
                if (n >= _samples.Length || _hasGap)
                    continue; // Keep draining so the stream does not overflow
                // The sample index counts from the scan start, so it has to match the number of samples recorded
                if (sample.Item1 != n)
                {
                    _hasGap = true;
                    continue;
                }
                _samples[n] = sample.Item2;
                _numRecorded = n + 1;
            }
        }

        public void Start(ProducerConsumer<Tuple<long, double>> queue)
        {
            if (_consumeTask != null)
                throw new InvalidOperationException("A recorder can only be started once");
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            _consumeTask = System.Threading.Tasks.Task.Run(() => ConsumeLoop(token, queue));
        }

        public void Stop()
        {
            if (_consumeTask == null || _consumeTask.IsCompleted)
                return;
            _cancellationTokenSource?.Cancel();
            _consumeTask.Wait();
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}
