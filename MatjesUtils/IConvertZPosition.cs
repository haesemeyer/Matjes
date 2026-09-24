using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesUtils
{
    /// <summary>
    /// Interface that needs to be implemented by calibration objects that relate
    /// the Z-Position to our objective Piezo to the z-position of scan mirrors
    /// </summary>
    public interface IConvertZPosition
    {
        public abstract double ConvertPiezoToZ(double v_piezo);
    }
}
