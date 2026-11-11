using System;

namespace Codetective.Terminal
{
    /// <summary>An elliptical paraboloid. Negative Z faces the viewer of a world-space Canvas.</summary>
    public static class CrtSurfaceMath
    {
        public static double Height(double x, double y, double halfWidth, double halfHeight, double bulge)
        {
            double nx = Math.Max(-1, Math.Min(1, x / halfWidth));
            double ny = Math.Max(-1, Math.Min(1, y / halfHeight));
            return bulge * (0.5 * (nx * nx + ny * ny) - 1);
        }

        /// <summary>Find the closest front-facing ray hit. Direction need not be normalized.</summary>
        public static bool TryIntersect(
            double ox, double oy, double oz, double dx, double dy, double dz,
            double halfWidth, double halfHeight, double bulge, bool requireInside, out double distance)
        {
            distance = 0;
            if (halfWidth <= 0 || halfHeight <= 0 || bulge < 0)
                return false;

            double widthSquared = halfWidth * halfWidth;
            double heightSquared = halfHeight * halfHeight;
            double a = 0.5 * bulge * (dx * dx / widthSquared + dy * dy / heightSquared);
            double b = bulge * (ox * dx / widthSquared + oy * dy / heightSquared) - dz;
            double c = bulge * (0.5 * (ox * ox / widthSquared + oy * oy / heightSquared) - 1) - oz;

            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) < 1e-12)
                    return false;
                return Accept(-c / b, ox, oy, dx, dy, dz, halfWidth, halfHeight, bulge,
                    requireInside, out distance);
            }

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
                return false;

            // This form avoids cancellation for nearly perpendicular rays.
            double q = -0.5 * (b + (b >= 0 ? 1 : -1) * Math.Sqrt(discriminant));
            double t1 = q / a;
            double t2 = Math.Abs(q) > 1e-12 ? c / q : -b / (2 * a);
            return Accept(Math.Min(t1, t2), ox, oy, dx, dy, dz, halfWidth, halfHeight, bulge,
                       requireInside, out distance) ||
                   Accept(Math.Max(t1, t2), ox, oy, dx, dy, dz, halfWidth, halfHeight, bulge,
                       requireInside, out distance);
        }

        private static bool Accept(double t, double ox, double oy, double dx, double dy, double dz,
            double halfWidth, double halfHeight, double bulge, bool requireInside, out double distance)
        {
            distance = 0;
            if (double.IsNaN(t) || double.IsInfinity(t) || t < 0)
                return false;

            double x = ox + t * dx;
            double y = oy + t * dy;
            if (requireInside && (Math.Abs(x) > halfWidth + 1e-6 || Math.Abs(y) > halfHeight + 1e-6))
                return false;

            // Reject the back of the monitor, including the far root of an oblique ray.
            double facing = dz - bulge * (x * dx / (halfWidth * halfWidth) + y * dy / (halfHeight * halfHeight));
            if (facing <= 1e-12)
                return false;

            distance = t;
            return true;
        }
    }
}
