using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesUtils
{
    /// <summary>
    /// The result of an ordinary least squares fit y = slope * x + offset
    /// </summary>
    public sealed class LinearFitResult
    {
        /// <summary>
        /// The slope of the fit
        /// </summary>
        public double Slope { get; }

        /// <summary>
        /// The offset (intercept) of the fit
        /// </summary>
        public double Offset { get; }

        /// <summary>
        /// The coefficient of determination of the fit. NaN if the y-values have no variance
        /// </summary>
        public double RSquared { get; }

        /// <summary>
        /// The residuals (y - predicted y) of each point, in the order the points were supplied
        /// </summary>
        public IReadOnlyList<double> Residuals { get; }

        /// <summary>
        /// The number of points used in the fit
        /// </summary>
        public int N => Residuals.Count;

        public LinearFitResult(double slope, double offset, double rSquared, IReadOnlyList<double> residuals)
        {
            Slope = slope;
            Offset = offset;
            RSquared = rSquared;
            Residuals = residuals;
        }

        /// <summary>
        /// Evaluates the fit at x
        /// </summary>
        public double Predict(double x)
        {
            return Slope * x + Offset;
        }
    }

    /// <summary>
    /// Ordinary least squares fitting of straight lines
    /// </summary>
    public static class LinearFit
    {
        /// <summary>
        /// Fits y = slope * x + offset by ordinary least squares, i.e. minimizing the squared error in y.
        /// Therefore x should be the variable that is set precisely and y the variable that carries measurement error.
        /// </summary>
        /// <param name="x">The independent values</param>
        /// <param name="y">The dependent values</param>
        /// <returns>The fit result</returns>
        /// <exception cref="ArgumentException">If x and y differ in length, have fewer than 2 points or x has no variance</exception>
        public static LinearFitResult Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            ArgumentNullException.ThrowIfNull(x, nameof(x));
            ArgumentNullException.ThrowIfNull(y, nameof(y));
            if (x.Count != y.Count)
                throw new ArgumentException("x and y need to have the same number of elements");
            int n = x.Count;
            if (n < 2)
                throw new ArgumentException("At least two points are required for a linear fit");

            double mean_x = 0, mean_y = 0;
            for (int i = 0; i < n; i++)
            {
                mean_x += x[i];
                mean_y += y[i];
            }
            mean_x /= n;
            mean_y /= n;

            // Use centered sums for numerical stability (piezo values are in the hundreds, voltages around 1)
            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - mean_x;
                double dy = y[i] - mean_y;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }
            if (sxx <= 0)
                throw new ArgumentException("All x values are identical, the slope is undefined");

            double slope = sxy / sxx;
            double offset = mean_y - slope * mean_x;

            double[] residuals = new double[n];
            double ss_res = 0;
            for (int i = 0; i < n; i++)
            {
                residuals[i] = y[i] - (slope * x[i] + offset);
                ss_res += residuals[i] * residuals[i];
            }
            double r_squared = syy > 0 ? 1 - ss_res / syy : double.NaN;
            return new LinearFitResult(slope, offset, r_squared, residuals);
        }
    }
}
