using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesUtils
{
    /// <summary>
    /// Interface that needs to be implemented by calibration objects that relate
    /// the Z-Position in um of our objective Piezo to the z-position in volts of scan mirrors
    /// </summary>
    public interface IConvertZPosition
    {
        /// <summary>
        /// Converts the piezo micron position to an appropriate z-voltage command on the z-mirrors
        /// </summary>
        /// <param name="um_piezo">The piezo position in microns</param>
        /// <returns>The mirror command voltage</returns>
        public abstract double ConvertPiezoToZ(double um_piezo);

        /// <summary>
        /// The maximum possible command mirror voltage
        /// </summary>
        public abstract double V_Max { get; }

        /// <summary>
        /// The minimum possible command mirror voltage
        /// </summary>
        public abstract double V_Min { get; }
    }
}
