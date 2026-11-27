using System;
using System.Collections.Generic;
using System.Linq;

namespace lcdb.Optic;

/// <summary>Bounds the separation over the complete radial aperture, including flat shoulders.</summary>
public static class SurfaceClearance
{
    private const int MaximumIntervals = 65535;
    private const int MaximumDepth = 48;

    public static void ValidatePositiveThickness(OpticalSurface front, OpticalSurface back,
        double thickness, double semiAperture, string label = "镜片")
    {
        if (!double.IsFinite(thickness) || thickness <= 0 || !double.IsFinite(semiAperture) || semiAperture <= 0)
            throw new InvalidOperationException(label + "的厚度和半口径必须为正的有限数值。");
        double frontLimit = ValidateSurface(front, semiAperture);
        double backLimit = ValidateSurface(back, semiAperture);
        // Beyond the last curved edge both profiles are flat and the separation is constant.
        var pending = new Stack<(double Lower, double Upper, int Depth)>();
        pending.Push((0, Math.Max(frontLimit, backLimit), 0));
        int visited = 0;
        while (pending.Count > 0)
        {
            var range = pending.Pop();
            if (++visited > MaximumIntervals)
                throw new InvalidOperationException(label + "的最小厚度无法可靠确认，请核对非球面参数或口径。");

            Interval separation = SeparationBounds(front, back, thickness, frontLimit, backLimit, range.Lower, range.Upper);
            if (!separation.IsFinite)
                throw new InvalidOperationException(label + "的矢高计算超出有效数值范围。");
            if (separation.Lower > 0) continue;

            double middle = range.Lower + (range.Upper - range.Lower) / 2;
            foreach (double radius in new[] { range.Lower, middle, range.Upper })
            {
                Interval sample = SeparationBounds(front, back, thickness, frontLimit, backLimit, radius, radius);
                if (!sample.IsFinite)
                    throw new InvalidOperationException(label + "的矢高计算超出有效数值范围。");
                if (sample.Upper <= 0)
                    throw new InvalidOperationException(label + "的表面相交或边厚非正，请核对口径与厚度。");
            }
            if (range.Depth >= MaximumDepth || middle <= range.Lower || middle >= range.Upper)
                throw new InvalidOperationException(label + "的最小厚度接近零或无法可靠确认，请核对非球面参数或口径。");
            pending.Push((middle, range.Upper, range.Depth + 1));
            pending.Push((range.Lower, middle, range.Depth + 1));
        }
    }

    private static double ValidateSurface(OpticalSurface surface, double semiAperture)
    {
        if (surface is not SphericalSurface && surface is not AsphericSurface)
            throw new InvalidOperationException("当前无法校验该表面类型的完整口径。");
        if (double.IsNaN(surface.Radius) || surface.Radius == 0 ||
            !double.IsFinite(surface.SemiAperture) || surface.SemiAperture < 0)
            throw new InvalidOperationException("表面半径或通光口径无效。");
        if (surface is AsphericSurface asphere && (!double.IsFinite(asphere.ConicConstant) ||
            !double.IsFinite(asphere.QuadraticCoefficient) || asphere.EvenCoefficients == null ||
            asphere.EvenCoefficients.Any(coefficient => !double.IsFinite(coefficient))))
            throw new InvalidOperationException("非球面系数必须为有限数值。");

        double height = surface.CurveRadiusOr(semiAperture);
        if (!double.IsFinite(height) || height <= 0 || height < surface.SemiAperture)
            throw new InvalidOperationException("曲面终止半径必须为正的有限值，且不小于逐面通光半径。");
        double conic = Conic(surface);
        if (double.IsFinite(surface.Radius) && conic > -1 &&
            height > Math.Abs(surface.Radius) / Math.Sqrt(1 + conic))
            throw new InvalidOperationException("口径超出表面可用范围，请修改导入口径。");
        Interval sag = ConicBounds(surface, 0, height) + PolynomialBounds(surface, null, 0, height);
        if (!sag.IsFinite || !double.IsFinite(surface.Sag(height)))
            throw new InvalidOperationException("表面矢高计算超出有效数值范围。");
        return height;
    }

    private static double Conic(OpticalSurface surface)
        => surface is AsphericSurface asphere ? asphere.ConicConstant : 0;

    private static Interval SeparationBounds(OpticalSurface front, OpticalSurface back, double thickness,
        double frontLimit, double backLimit, double lower, double upper)
    {
        double frontLower = Math.Min(frontLimit, lower), frontUpper = Math.Min(frontLimit, upper);
        double backLower = Math.Min(backLimit, lower), backUpper = Math.Min(backLimit, upper);
        bool sameArgument = frontLower == backLower && frontUpper == backUpper;
        Interval basis = sameArgument && front.Radius == back.Radius && Conic(front) == Conic(back)
            ? Interval.Zero
            : ConicBounds(back, backLower, backUpper) - ConicBounds(front, frontLower, frontUpper);
        // Subtract coefficients before evaluation when both faces use the same radius variable.
        // This preserves cancellation in thin elements with matching curved faces.
        Interval polynomial = sameArgument
            ? PolynomialBounds(back, front, backLower, backUpper)
            : PolynomialBounds(back, null, backLower, backUpper) - PolynomialBounds(front, null, frontLower, frontUpper);
        return Interval.Point(thickness) + basis + polynomial;
    }

    private static Interval ConicBounds(OpticalSurface surface, double lower, double upper)
    {
        if (double.IsInfinity(surface.Radius)) return Interval.Zero;
        Interval first = ConicSag(surface, lower), last = ConicSag(surface, upper);
        // A valid spherical/conic base is monotonic for nonnegative radius.
        return new Interval(Math.Min(first.Lower, last.Lower), Math.Max(first.Upper, last.Upper));
    }

    private static Interval ConicSag(OpticalSurface surface, double radius)
    {
        if (radius == 0) return Interval.Zero;
        Interval curvature = Interval.Point(1) / Interval.Point(surface.Radius);
        Interval heightSquared = Interval.Point(radius) * Interval.Point(radius);
        Interval radicand = Interval.Point(1) -
            (Interval.Point(1) + Interval.Point(Conic(surface))) * curvature * curvature * heightSquared;
        if (radicand.Upper < 0) throw new InvalidOperationException("口径超出表面可用范围。");
        // Domain validation is separate. Rounding bounds at the rim may straddle zero.
        var root = new Interval(Math.Max(0, Math.BitDecrement(Math.Sqrt(Math.Max(0, radicand.Lower)))),
            Math.BitIncrement(Math.Sqrt(Math.Max(0, radicand.Upper))));
        return curvature * heightSquared / (Interval.Point(1) + root);
    }

    private static int CoefficientCount(OpticalSurface surface)
        => surface is AsphericSurface asphere ? asphere.EvenCoefficients.Length + 1 : 0;

    private static double Coefficient(OpticalSurface surface, int index)
    {
        if (surface is not AsphericSurface asphere) return 0;
        if (index == 0) return asphere.QuadraticCoefficient;
        return index <= asphere.EvenCoefficients.Length ? asphere.EvenCoefficients[index - 1] : 0;
    }

    private static Interval PolynomialBounds(OpticalSurface surface, OpticalSurface subtract,
        double lower, double upper)
    {
        int count = Math.Max(CoefficientCount(surface), CoefficientCount(subtract));
        Interval squaredRadius = new Interval(lower, upper) * new Interval(lower, upper);
        Interval polynomial = Interval.Zero;
        for (int index = count - 1; index >= 0; index--)
        {
            double coefficient = Coefficient(surface, index), other = Coefficient(subtract, index);
            Interval difference = coefficient == other ? Interval.Zero : Interval.Point(coefficient) - Interval.Point(other);
            polynomial = polynomial * squaredRadius + difference;
        }
        return polynomial * squaredRadius;
    }

    /// <summary>Outward-rounded arithmetic prevents a rounded positive lower bound from proving clearance.</summary>
    private readonly record struct Interval(double Lower, double Upper)
    {
        public static Interval Zero => new(0, 0);
        public static Interval Point(double value) => new(value, value);
        public bool IsFinite => double.IsFinite(Lower) && double.IsFinite(Upper);
        private bool IsZero => Lower == 0 && Upper == 0;

        public static Interval operator +(Interval left, Interval right)
        {
            if (left.IsZero) return right;
            if (right.IsZero) return left;
            return new(Math.BitDecrement(left.Lower + right.Lower), Math.BitIncrement(left.Upper + right.Upper));
        }

        public static Interval operator -(Interval left, Interval right)
            => left + new Interval(-right.Upper, -right.Lower);

        public static Interval operator *(Interval left, Interval right)
        {
            if (left.IsZero || right.IsZero) return Zero;
            double a = left.Lower * right.Lower, b = left.Lower * right.Upper;
            double c = left.Upper * right.Lower, d = left.Upper * right.Upper;
            return new(Math.BitDecrement(Math.Min(Math.Min(a, b), Math.Min(c, d))),
                Math.BitIncrement(Math.Max(Math.Max(a, b), Math.Max(c, d))));
        }

        public static Interval operator /(Interval left, Interval right)
        {
            if (right.Lower <= 0 && right.Upper >= 0)
                throw new InvalidOperationException("表面计算无法可靠确认，分母包含零。");
            var reciprocal = new Interval(Math.BitDecrement(1 / right.Upper), Math.BitIncrement(1 / right.Lower));
            return left * reciprocal;
        }
    }
}
