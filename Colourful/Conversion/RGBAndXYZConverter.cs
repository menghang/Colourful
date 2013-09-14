﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Colourful.ChromaticAdaptation;
using Colourful.Colors;
using Colourful.RGBWorkingSpaces;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Generic;

namespace Colourful.Conversion
{
    /// <summary>
    /// Converts from RGB to XYZ and backwards
    /// </summary>
    public class RGBAndXYZConverter : IColorConverter<RGBColor, XYZColor>, IColorConverter<XYZColor, RGBColor>
    {
        /// <summary>
        /// Uses <see cref="DefaultChromaticAdaptation"/> if needed
        /// </summary>
        public RGBAndXYZConverter()
        {
            ChromaticAdaptation = DefaultChromaticAdaptation;
        }

        /// <summary>
        /// Uses specified <see cref="IChromaticAdaptation"/> if needed
        /// </summary>
        public RGBAndXYZConverter(IChromaticAdaptation chromaticAdaptation)
        {
            ChromaticAdaptation = chromaticAdaptation;
        }

        public IChromaticAdaptation ChromaticAdaptation { get; private set; }

        /// <summary>
        /// Bradford chromatic adaptation, used when chromatic adaptation for reference white adjustation is not specified explicitly.
        /// </summary>
        public IChromaticAdaptation DefaultChromaticAdaptation
        {
            get { return new BradfordChromaticAdaptation(); }
        }

        #region RGB to XYZ

        /// <summary>
        /// Converts RGB to XYZ, target reference white is taken from RGB working space
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public XYZColor Convert(RGBColor input)
        {
            IRGBWorkingSpace workingSpace = input.WorkingSpace;

            Vector<double> rgb = UncompandVector(input);
            Matrix<double> matrix = GetRGBToXYZMatrix(workingSpace);

            Vector<double> xyz = matrix * rgb;

            double x, y, z;
            xyz.AssignVariables(out x, out y, out z);

            XYZColorBase referenceWhite = workingSpace.ReferenceWhite;

            return new XYZColor(x, y, z, referenceWhite);
        }

        /// <summary>
        /// Converts RGB to XYZ, output color is adjusted to the given reference white (Bradford adaptation)
        /// </summary>
        /// <param name="input"></param>
        /// <param name="referenceWhite"></param>
        /// <returns></returns>
        public XYZColor Convert(RGBColor input, XYZColorBase referenceWhite)
        {
            XYZColor converted = Convert(input);

            if (converted.ReferenceWhite == referenceWhite)
                return converted;

            XYZColor output = ChromaticAdaptation.Transform(converted, referenceWhite);
            return output;
        }

        private static Matrix<double> GetRGBToXYZMatrix(IRGBWorkingSpace workingSpace)
        {
            // http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html

            RGBPrimariesChromaticityCoordinates chromaticity = workingSpace.ChromaticityCoordinates;
            double xr = chromaticity.R.x, xg = chromaticity.G.x, xb = chromaticity.B.x,
                   yr = chromaticity.R.y, yg = chromaticity.G.y, yb = chromaticity.B.y;

            double Sr, Sg, Sb;

            double Xr = xr / yr;
            const double Yr = 1;
            double Zr = (1 - xr - yr) / yr;

            double Xg = xg / yg;
            const double Yg = 1;
            double Zg = (1 - xg - yg) / yg;

            double Xb = xb / yb;
            const double Yb = 1;
            double Zb = (1 - xb - yb) / yb;

            {
                Matrix<double> S = DenseMatrix.OfRows(3, 3, new[]
                    {
                        new[] { Xr, Xg, Xb },
                        new[] { Yr, Yg, Yb },
                        new[] { Zr, Zg, Zb },
                    }).Inverse();

                Vector<double> W = workingSpace.ReferenceWhite.Vector;

                (S * W).AssignVariables(out Sr, out Sg, out Sb);
            }

            DenseMatrix M = DenseMatrix.OfRows(3, 3, new[]
                {
                    new[] { Sr * Xr, Sg * Xg, Sb * Xb },
                    new[] { Sr * Yr, Sg * Yg, Sb * Yb },
                    new[] { Sr * Zr, Sg * Zg, Sb * Zb },
                });

            return M;
        }

        /// <summary>
        /// Applying the working space inverse companding function (<see cref="IRGBWorkingSpace.Companding"/>) to RGB vector.
        /// </summary>
        /// <param name="rgbColor"></param>
        /// <returns></returns>
        private static Vector<double> UncompandVector(RGBColor rgbColor)
        {
            var inverseCompanding = rgbColor.WorkingSpace.Companding;
            Vector<double> compandedVector = rgbColor.Vector;
            DenseVector uncompandedVector = DenseVector.OfEnumerable(compandedVector.Select(inverseCompanding.InverseCompanding));
            return uncompandedVector;
        }

        #endregion

        #region XYZ to RGB

        /// <remarks>
        /// The target RGB working space is <see cref="RGBColor.DefaultWorkingSpace"/>.
        /// If the source XYZ color reference white doesn't match the target RGB working space reference white, it is adjusted using <see cref="ChromaticAdaptation"/>.
        /// </remarks>
        public RGBColor Convert(XYZColor input)
        {
            var result = Convert(input, RGBColor.DefaultWorkingSpace);
            return result;
        }

        /// <remarks>
        /// If the source XYZ color reference white doesn't match the target RGB working space reference white, it is adjusted using <see cref="ChromaticAdaptation"/>.
        /// </remarks>
        public RGBColor Convert(XYZColor input, IRGBWorkingSpace workingSpace)
        {
            Vector<double> inputVector = input.ReferenceWhite != workingSpace.ReferenceWhite 
                ? ChromaticAdaptation.TransformNonCropped(input, workingSpace.ReferenceWhite).Vector
                : input.Vector;

            var uncompandedVector = GetXYZToRGBMatrix(workingSpace) * inputVector;
            var result = CompandVector(uncompandedVector, workingSpace);
            return result;
        }

        private static Matrix<double> GetXYZToRGBMatrix(IRGBWorkingSpace workingSpace)
        {
            return GetRGBToXYZMatrix(workingSpace).Inverse();
        }

        /// <summary>
        /// Applying the working space companding function (<see cref="IRGBWorkingSpace.Companding"/>) to uncompanded vector.
        /// </summary>
        /// <returns></returns>
        private static RGBColor CompandVector(Vector<double> uncompandedVector, IRGBWorkingSpace workingSpace)
        {
            var companding = workingSpace.Companding;
            DenseVector compandedVector = DenseVector.OfEnumerable(uncompandedVector.Select(companding.Companding));
            double R, G, B;
            compandedVector.AssignVariables(out R, out G, out B);

            R = R.CropRange(0, 1);
            G = G.CropRange(0, 1);
            B = B.CropRange(0, 1);

            var result = new RGBColor(R, G, B, workingSpace);
            return result;
        }

        #endregion
    }
}