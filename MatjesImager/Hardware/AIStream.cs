using MatjesImager.Properties;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    public class AIStream(int buffer_size, int samples_to_average) : IDisposable
    {

        #region Members

        /// <summary>
        /// Producer consumer to make read samples available together with their absolute index
        /// </summary>
        private readonly ProducerConsumer<Tuple<long, double>> _hardwareReads = new ProducerConsumer<Tuple<long, double>>(buffer_size);

        /// <summary>
        /// Defines the number of samples that will be averaged to produce one sample in _hardwareReads
        /// </summary>
        private readonly int _samplesToAverage = samples_to_average;

        #endregion

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




        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
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
