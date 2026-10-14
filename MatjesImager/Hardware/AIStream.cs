using MatjesImager.Properties;
using MatjesUtils;
using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    public class AIStream : IDisposable
    {

        #region Members

        /// <summary>
        /// Producer consumer to make read samples available together with their absolute index
        /// </summary>
        private readonly ProducerConsumer<Tuple<long, double>> _hardwareReads;

        /// <summary>
        /// Defines the number of samples that will be averaged to produce one sample in _hardwareReads
        /// </summary>
        private readonly int _samplesToAverage;

        /// <summary>
        /// The overall index of the sample put onto the queue
        /// </summary>
        private long _globalSampleIndex;

        private bool _isRunning = false;

        /// <summary>
        /// Task for analog-in reads
        /// </summary>
        private NationalInstruments.DAQmx.Task? _aiTask;

        private CancellationTokenSource? _cancellationTokenSource;

        #endregion

        /// <summary>
        /// Creates a new AIStream object
        /// </summary>
        /// <param name="buffer_size">The size of the transfer buffer (has to be at least 1)</param>
        /// <param name="samples_to_average">The number of samples to average on each read (has to be at least 1)</param>
        public AIStream(int buffer_size, int samples_to_average)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(buffer_size, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(samples_to_average, 1);

            _hardwareReads = new ProducerConsumer<Tuple<long, double>>(buffer_size);
            _samplesToAverage = samples_to_average;
        }

        #region Properties

        /// <summary>
        /// Producer consumer to make read samples available together with their absolute index
        /// </summary>
        public ProducerConsumer<Tuple<long, double>> HardwareReads
        {
            get
            {
                return _hardwareReads;
            }
        }

        /// <summary>
        /// Defines the number of samples that will be averaged to produce one sample in _hardwareReads
        /// </summary>
        public int SamplesToAverage
        {
            get
            {
                return _samplesToAverage;
            }
        }

        // Physcial channel names, convenience accessors

        private static string PiezoPositionRead => $"{Settings.Default.ZAndCamBoard}/{Settings.Default.AIPiezo}";

        private static string Trigger => $"/{Settings.Default.ZAndCamBoard}/{Settings.Default.CameraTriggerCounter}InternalOutput";

        #endregion

        #region Threadprocs

        private void ReadLoop(CancellationToken token, int samplesToRead)
        {
            if (_aiTask == null)
                return;
            AnalogSingleChannelReader reader = new AnalogSingleChannelReader(_aiTask.Stream);
            double[] readBuffer = new double[samplesToRead];
            int samples_read;
            bool incomplete_read = false;
            while (!token.IsCancellationRequested && !incomplete_read)
            {
                if(_aiTask.Stream.AvailableSamplesPerChannel >= samplesToRead)
                {
                    reader.MemoryOptimizedReadMultiSample(samplesToRead, ref readBuffer, out samples_read);// NOTE: Given our if statement above, at this point samples_read==samplesToRead unless something went wrong
                    int sample_index = 0;
                    while(sample_index  < samples_read)
                    {
                        double value = 0;
                        for (int j = 0; j < _samplesToAverage; j++)
                        {
                            value += readBuffer[sample_index];
                            sample_index++;
                        }
                        // Average and put the new value together with the index onto the queue
                        value /= _samplesToAverage;
                        _hardwareReads.Produce(new Tuple<long, double>(_globalSampleIndex++, value));
                    }
                }
            }
        }

        #endregion


        #region Methods

        /// <summary>
        /// Start AI reading
        /// </summary>
        /// <param name="sampleRate">The sample rate at which to read from analog in</param>
        /// <exception cref="InvalidOperationException">Raised if calling start while running</exception>
        public void Start(int sampleRate)
        {
            if (_isRunning)
                throw new InvalidOperationException("Invoked Start while AIStream is running");
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _globalSampleIndex = 0;
            // Calculate our samples to read - we aim to read 10 times per second and always read a sample number that is divisible by samples to average
            int samplesToRead = sampleRate / 10;
            samplesToRead -= samplesToRead % _samplesToAverage;
            if (samplesToRead < _samplesToAverage)
            {
                samplesToRead = _samplesToAverage;
            }
            // Set up our analog input task
            _aiTask = new NationalInstruments.DAQmx.Task("HardwareRead");
            _aiTask.AIChannels.CreateVoltageChannel(PiezoPositionRead, "piezo_read", AITerminalConfiguration.Differential, 0, 10, AIVoltageUnits.Volts);
            _aiTask.Timing.ConfigureSampleClock("", sampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            // Set input buffer size to 2 seconds worth of data
            _aiTask.Stream.ConfigureInputBuffer(2 * sampleRate);
            // Set up our trigger
            _aiTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger(Trigger, DigitalEdgeStartTriggerEdge.Rising);
            // Start our task which will wait on the trigger
            _aiTask.Start();
            // Start the read method
            System.Threading.Tasks.Task.Run(() => ReadLoop(_cancellationTokenSource.Token, samplesToRead));
            _isRunning = true;
        }

        /// <summary>
        /// Stop AI reading
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;
            _cancellationTokenSource?.Cancel();
            _aiTask?.Dispose();
            _isRunning = false;
        }

        #endregion


        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _cancellationTokenSource?.Cancel();
                    _aiTask?.Dispose();
                    _cancellationTokenSource?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
