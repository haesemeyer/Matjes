using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesUtils
{
    public class LinearZConverter : IConvertZPosition
    {
        private double _v_max;

        private double _v_min;

        /// <summary>
        /// The slope of the relationship from piezo microns to z-mirror voltage
        /// </summary>
        public double Slope { get; private set; }

        /// <summary>
        /// The offset of the relationship from piezo microns to z-mirror voltage
        /// </summary>
        public double Offset { get; private set; }

        /// <summary>
        /// The maximum possible command mirror voltage
        /// </summary>
        public double V_Max => _v_max;

        /// <summary>
        /// The minimum possible command mirror voltage
        /// </summary>
        public double V_Min => _v_min;

        /// <summary>
        /// Generates a new linear piezo-scanmirror converter such that: v_z = um_p * slope + offset
        /// </summary>
        /// <param name="slope">The slope of the relationship</param>
        /// <param name="offset">The offset</param>
        /// <param name="v_max">The maximum allowed command voltage for the mirrors</param>
        /// <param name="v_min">The minimum allowed command voltage for the mirrors</param>
        public LinearZConverter(double slope, double offset, double v_max, double v_min)
        {
            Slope = slope;
            Offset = offset;
            _v_max = v_max;
            _v_min = v_min;
        }

        public double ConvertPiezoToZ(double um_piezo)
        {
            double retval = um_piezo * Slope + Offset;
            if (retval > V_Max)
                return V_Max;
            if (retval < V_Min)
                return V_Min;
            return retval;
        }
    }
}
