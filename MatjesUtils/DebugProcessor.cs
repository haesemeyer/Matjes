using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace MatjesUtils
{
    public class DebugProcessor : IAnalogProcessor<Tuple<long, double>>, IDisposable
    {
        private bool _isRunning = false;
        private CancellationTokenSource? _cancellationTokenSource;

        private System.Threading.Tasks.Task? _writeTask;

        private void ProcessLoop(CancellationToken token, ProducerConsumer<Tuple<long, double>> queue)
        {
            while (!token.IsCancellationRequested)
            {
                Tuple<long, double> read_value;
                try
                {
                    read_value = queue.Consume(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                System.Diagnostics.Debug.WriteLineIf(read_value.Item1 % 1000 == 0, $"Sample: {read_value.Item1}; Value: {read_value.Item2}.");
            }
        }

        public void Start(ProducerConsumer<Tuple<long, double>> queue)
        {
            if (_isRunning)
                throw new InvalidOperationException("Invoked Start while Processor is running");
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _writeTask = new System.Threading.Tasks.Task(() => ProcessLoop(_cancellationTokenSource.Token, queue));
            _writeTask.Start();
        }

        public void Stop()
        {
            if (_isRunning)
                return;
            _cancellationTokenSource?.Cancel();
            _writeTask?.Wait();
            _writeTask?.Dispose();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
