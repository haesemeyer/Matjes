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
                System.Diagnostics.Debug.WriteLine($"Sample: {read_value.Item1}; Value: {read_value.Item2}.");
            }
        }

        public void Start(ProducerConsumer<Tuple<long, double>> queue)
        {
            if (_isRunning)
                throw new InvalidOperationException("Invoked Start while Processor is running");
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            System.Threading.Tasks.Task.Run(() => ProcessLoop(_cancellationTokenSource.Token, queue));
        }

        public void Stop()
        {
            if (_isRunning)
                return;
            _cancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
